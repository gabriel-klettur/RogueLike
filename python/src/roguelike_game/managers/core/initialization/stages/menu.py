from __future__ import annotations

import logging

from roguelike_engine.audio.config import load_audio_catalog
from roguelike_game.config.audio_config import AudioConfig
from roguelike_game.config.input_config import InputConfig
from roguelike_game.managers.core.audio_manager import AudioManager
from roguelike_game.managers.menu import MenuManager
from roguelike_game.managers.player.class_selector_manager import (
    ClassSelectorManager,
)
from roguelike_game.managers.player.player_manager import PlayerManager

from ..types import InitContext

logger = logging.getLogger(__name__)


def init_menu(ctx: InitContext) -> None:
    g = ctx.game
    g.input_config = InputConfig()
    if not hasattr(g, "audio_config") or g.audio_config is None:
        g.audio_config = AudioConfig()
    if not hasattr(g, "audio_manager") or g.audio_manager is None:
        g.audio_manager = AudioManager(g.audio_config)
    g.menu = MenuManager(
        g,
        g.state,
        g.screen,
        g.input_config,
        audio_config=g.audio_config,
        audio_manager=g.audio_manager,
        audio_bus=getattr(g, "audio_bus", None),
        font_size=18,
    )
    try:
        if getattr(g, "menu_music_prestarted", False):
            setattr(g.menu, "_music_already_playing_externally", True)
    except Exception:
        pass
    try:
        fx = getattr(g, "startup_ui_fx", {}) or {}
        g.menu._startup_flash_enabled = bool(fx.get("flash_enabled", True))
        g.menu._startup_flash_at_s = float(fx.get("flash_at_s", 6.0))
        g.menu._startup_flash_duration_s = float(fx.get("flash_duration_s", 0.25))
        g.menu._startup_flash_color_rgba = tuple(
            fx.get("flash_color_rgba", (255, 255, 255, 255))
        )
        g.menu._startup_flash_ease = str(fx.get("flash_ease", "linear"))
        g.menu._startup_flash_trigger = str(
            fx.get("flash_trigger", "time")
        )
        g.menu._startup_enable_cycle_after_flash = bool(
            fx.get("enable_carousel_after_flash", True)
        )
        g.menu._startup_block_cycle_until_flash = bool(
            fx.get("block_carousel_until_flash", True)
        )
        g.menu._startup_fade_in_ms = int(fx.get("fade_in_ms", 300))
        try:
            g.menu._music_loop = bool(fx.get("loop", True))
        except Exception:
            pass
        if (not g.menu._startup_block_cycle_until_flash) or (
            not g.menu._startup_flash_enabled
        ):
            g.menu._bg_cycle_enabled = True
    except Exception:
        pass
    try:
        g.menu.set_backgrounds(
            [
                "assets/ui/intro/Intro_elven.png",
                "assets/ui/intro/Intro_drwaft.png",
                "assets/ui/intro/intro_mague.png",
                "assets/ui/intro/Intro_valkyrie.png",
                "assets/ui/intro/Intro_barbarian.png",
            ],
            interval_s=2.0,
            transition_s=0.6,
            slide_px=24,
        )
    except Exception:
        pass
    try:
        try:
            mv = float(g.audio_config.get("music")) if g.audio_config else 0.6
        except Exception:
            mv = 0.6
        intro_path = None
        try:
            catalog = load_audio_catalog()
            defaults = catalog.get_default_music() if catalog else {}
            startup_id = (defaults or {}).get("startup_track_id")
            if startup_id:
                intro_path = catalog.track_path(startup_id)
        except Exception:
            intro_path = None
        g.menu.set_music(
            intro_path or "assets/audio/music/intro_theme.mp3",
            loop=True,
            volume=(mv if mv is not None else g.menu._music_volume),
        )
    except Exception:
        pass
    try:
        g.menu.set_logo(
            "assets/ui/intro/game_name.png",
            max_width_ratio=1.0,
            max_height_ratio=1.0,
            gap_px=12,
        )
    except Exception:
        pass
    try:
        g.menu.enable_press_to_start()
    except Exception:
        pass
    try:
        g.menu.set_mode("start")
        g.menu.show_menu = True
    except Exception:
        g.menu.mode = "start"
        g.menu.show_menu = True
    g.class_selector = ClassSelectorManager(g.state, g.input_config, g.screen)
    g.player_manager = PlayerManager(g.ecs.ecs_world)
