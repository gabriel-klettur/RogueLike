from __future__ import annotations

import logging
from typing import Optional

import pygame

from roguelike_engine.audio.config import load_audio_catalog

logger = logging.getLogger(__name__)


class MusicManager:
    """Encapsula la música del menú y aplica cambios en vivo.

    - Reproduce música del menú cuando el menú está visible en modos start/load_list.
    - Detiene con fade-out configurado al salir.
    - Aplica cambios de volumen y delega en audio_bus si existe.
    """

    def __init__(self, *, audio_manager=None, audio_bus=None) -> None:
        self.music_path: Optional[str] = None
        self._music_loop: bool = True
        self._music_volume: float = 0.6
        self._music_active: bool = False
        self.audio_manager = audio_manager
        self.audio_bus = audio_bus

        # Si el launcher ya inició música, no duplicar arranque
        self.external_already_playing: bool = False
        # Fade-in inicial opcional (ms) cuando el menú aparece
        self.startup_fade_in_ms: int = 300

    # ---------------- Config helpers ----------------
    @staticmethod
    def _get_menu_fade_out_ms() -> int:
        try:
            catalog = load_audio_catalog()
            d = getattr(catalog, "defaults", {}) or {}
            music_d = d.get("music", {}) or {}
            v = int(music_d.get("menu_fade_out_ms", 500))
            return max(0, v)
        except Exception:
            return 500

    @staticmethod
    def _get_crossfade_ms() -> int:
        try:
            catalog = load_audio_catalog()
            d = getattr(catalog, "defaults", {}) or {}
            music_d = d.get("music", {}) or {}
            v = int(music_d.get("crossfade_ms", 600))
            return max(0, v)
        except Exception:
            return 600

    # ---------------- Public API ----------------
    def set_music(self, path: Optional[str], *, loop: bool = True, volume: float = 0.6) -> None:
        self.music_path = path
        self._music_loop = bool(loop)
        self._music_volume = max(0.0, min(1.0, float(volume)))
        self._music_active = False

    def ensure_for_menu(self, *, show_menu: bool, mode: str, game) -> None:
        try:
            if self.music_path and show_menu and mode in ("start", "load_list"):
                if not self._music_active:
                    if self.external_already_playing:
                        self._music_active = True
                        return
                    if getattr(self, "audio_bus", None) is not None:
                        try:
                            fade_ms = int(getattr(self, "startup_fade_in_ms", 300))
                            self.audio_bus.play_music(
                                path=self.music_path,
                                loop=self._music_loop,
                                volume=self._music_volume,
                                fade_in_ms=fade_ms,
                            )
                            self._music_active = True
                            try:
                                import time as _time

                                if getattr(game, "intro_music_started_at", None) is None:
                                    game.intro_music_started_at = _time.time()
                            except Exception:
                                pass
                            return
                        except Exception:
                            pass
                    pygame.mixer.music.load(self.music_path)
                    pygame.mixer.music.set_volume(self._music_volume)
                    loops = -1 if self._music_loop else 0
                    try:
                        fade_ms = int(getattr(self, "startup_fade_in_ms", 300))
                        if fade_ms > 0:
                            pygame.mixer.music.play(loops, fade_ms=fade_ms)
                        else:
                            pygame.mixer.music.play(loops)
                    except Exception:
                        pygame.mixer.music.play(loops)
                    self._music_active = True
                    try:
                        import time as _time

                        if getattr(game, "intro_music_started_at", None) is None:
                            game.intro_music_started_at = _time.time()
                    except Exception:
                        pass
            else:
                if self._music_active:
                    try:
                        fade = int(self._get_menu_fade_out_ms())
                        if getattr(self, "audio_bus", None) is not None:
                            self.audio_bus.stop_music(fade_ms=fade)
                        else:
                            if fade > 0:
                                pygame.mixer.music.fadeout(fade)
                            else:
                                pygame.mixer.music.stop()
                    except Exception:
                        try:
                            pygame.mixer.music.stop()
                        except Exception:
                            pass
                    self._music_active = False
        except Exception:
            pass

    def stop_music(self, fade_ms: Optional[int] = None) -> None:
        try:
            if self._music_active:
                if getattr(self, "audio_bus", None) is not None:
                    try:
                        fade = self._get_menu_fade_out_ms() if fade_ms is None else int(fade_ms)
                        self.audio_bus.stop_music(fade_ms=fade)
                    except Exception:
                        pass
                else:
                    fade = self._get_menu_fade_out_ms() if fade_ms is None else int(fade_ms)
                    if fade > 0:
                        pygame.mixer.music.fadeout(fade)
                    else:
                        pygame.mixer.music.stop()
        finally:
            self._music_active = False

    def on_audio_change(self, kind: str, value: float) -> None:
        v = max(0.0, min(1.0, float(value)))
        if kind == "music":
            self._music_volume = v
            try:
                if getattr(self, "audio_bus", None) is not None:
                    self.audio_bus.set_music_volume(v)
                else:
                    pygame.mixer.music.set_volume(v)
            except Exception:
                pass
            try:
                if self.audio_manager is not None:
                    self.audio_manager.set_music_volume(v)
            except Exception:
                pass
        elif kind == "sfx":
            try:
                if getattr(self, "audio_bus", None) is not None:
                    self.audio_bus.set_sfx_volume(v)
                if self.audio_manager is not None:
                    self.audio_manager.set_sfx_volume(v)
            except Exception:
                pass
        elif kind == "ambient":
            try:
                if getattr(self, "audio_bus", None) is not None:
                    self.audio_bus.set_ambient_volume(v)
                if self.audio_manager is not None:
                    self.audio_manager.set_ambient_volume(v)
            except Exception:
                pass
