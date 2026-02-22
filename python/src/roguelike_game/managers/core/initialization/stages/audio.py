from __future__ import annotations

import logging

from roguelike_engine.audio.config import load_audio_catalog
from roguelike_engine.audio.service import AudioBus, AudioService

from ..types import InitContext

logger = logging.getLogger(__name__)


def init_audio(ctx: InitContext) -> None:
    """Inicializa el servicio de audio (hilo) y registra un AudioBus global."""
    g = ctx.game
    try:
        from roguelike_game.config.audio_config import AudioConfig  # local import to avoid cycles
        from roguelike_game.managers.core.audio_manager import AudioManager

        if not hasattr(g, "audio_config") or g.audio_config is None:
            g.audio_config = AudioConfig()
        if not hasattr(g, "audio_manager") or g.audio_manager is None:
            g.audio_manager = AudioManager(g.audio_config)
    except Exception:
        pass
    try:
        catalog = load_audio_catalog()
    except Exception:
        catalog = None
    svc = AudioService(catalog)
    try:
        svc.start()
    except Exception:
        pass
    bus = AudioBus(svc)
    try:
        from roguelike_engine.audio.api import set_bus as _set_audio_bus

        _set_audio_bus(bus)
    except Exception:
        pass
    try:
        mv = (
            float(getattr(getattr(g, "audio_config", None), "get", lambda *_: 0.6)("music"))
            if getattr(g, "audio_config", None)
            else 0.6
        )
    except Exception:
        mv = 0.6
    try:
        sv = (
            float(getattr(getattr(g, "audio_config", None), "get", lambda *_: 0.7)("sfx"))
            if getattr(g, "audio_config", None)
            else 0.7
        )
    except Exception:
        sv = 0.7
    try:
        av = (
            float(
                getattr(getattr(g, "audio_config", None), "get", lambda *_: 0.6)(
                    "ambient"
                )
            )
            if getattr(g, "audio_config", None)
            else 0.6
        )
    except Exception:
        av = 0.6
    try:
        if getattr(g, "audio_config", None) is None and catalog is not None and getattr(
            catalog, "groups", None
        ):
            mv = float((catalog.groups.get("music") or {}).get("volume", mv))
            sv = float((catalog.groups.get("sfx") or {}).get("volume", sv))
            av = float((catalog.groups.get("ambient") or {}).get("volume", av))
    except Exception:
        pass
    try:
        bus.set_music_volume(mv)
        bus.set_sfx_volume(sv)
        bus.set_ambient_volume(av)
    except Exception:
        pass
    g.audio_service = svc
    g.audio_bus = bus
    try:
        from roguelike_engine.audio.api import set_apply_hook as _set_apply_hook

        def _apply_now():
            try:
                aq = g.ecs.ecs_world.components.setdefault("AudioEventQueue", [])
                aq.append({"type": "reload_audio_catalog"})
            except Exception:
                pass

        _set_apply_hook(_apply_now)
    except Exception:
        pass
    try:
        import time as _time

        startup_cfg = {}
        try:
            startup_cfg = (getattr(catalog, "defaults", {}) or {}).get("startup", {}) or {}
        except Exception:
            startup_cfg = {}
        play_on_boot = bool(startup_cfg.get("play_on_boot", True))
        fade_in_ms = int(startup_cfg.get("fade_in_ms", 300))
        startup_loop = bool(startup_cfg.get("loop", True))
        flash_enabled = bool(startup_cfg.get("flash_enabled", True))
        flash_at_s = float(startup_cfg.get("flash_at_s", 6.0))
        flash_duration_s = float(startup_cfg.get("flash_duration_s", 0.25))
        flash_color_rgba = tuple(startup_cfg.get("flash_color_rgba", [255, 255, 255, 255]))
        flash_ease = str(startup_cfg.get("flash_ease", "linear"))
        flash_trigger = str(startup_cfg.get("flash_trigger", "time"))
        enable_cycle_after_flash = bool(startup_cfg.get("enable_carousel_after_flash", True))
        block_cycle_until_flash = bool(startup_cfg.get("block_carousel_until_flash", True))
        g.startup_ui_fx = {
            "flash_at_s": flash_at_s,
            "flash_duration_s": flash_duration_s,
            "flash_enabled": flash_enabled,
            "flash_color_rgba": flash_color_rgba,
            "flash_ease": flash_ease,
            "flash_trigger": flash_trigger,
            "enable_carousel_after_flash": enable_cycle_after_flash,
            "block_carousel_until_flash": block_cycle_until_flash,
            "fade_in_ms": fade_in_ms,
            "loop": startup_loop,
        }
        if play_on_boot:
            startup_id = None
            intro_path = None
            try:
                defaults = catalog.get_default_music() if catalog else {}
                startup_id = (defaults or {}).get("startup_track_id")
            except Exception:
                startup_id = None
            if startup_id:
                bus.play_music(
                    track_id=startup_id, loop=startup_loop, volume=mv, fade_in_ms=fade_in_ms
                )
            else:
                intro_path = "assets/audio/music/intro_theme.mp3"
                bus.play_music(
                    path=intro_path, loop=startup_loop, volume=mv, fade_in_ms=fade_in_ms
                )
            g.intro_music_started_at = _time.time()
            g.menu_music_prestarted = True
    except Exception:
        pass
