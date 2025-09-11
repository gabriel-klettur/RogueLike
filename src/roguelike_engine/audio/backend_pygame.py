from __future__ import annotations
import pygame
from typing import Optional, Dict, Any

class PygameAudioBackend:
    """
    Backend de audio basado en pygame.mixer. Encapsula llamadas directas a mixer/music.
    Limitaciones: pygame.mixer.music es global (una sola pista). Crossfade se simula con
    fadeout + fadein secuenciales.
    """
    def __init__(self) -> None:
        self._music_volume: float = 0.6
        self._sfx_volume: float = 0.7
        self._ambient_volume: float = 0.6
        self._channels_per_group: Dict[str, list[int]] = {
            'sfx': [], 'ui': [], 'combat': [], 'ambient': []
        }
        self._channels: Dict[int, pygame.mixer.Channel] = {}
        self._max_channels: int = 32

    def init(self) -> None:
        try:
            if not pygame.mixer.get_init():
                pygame.mixer.init()
        except Exception:
            # Intentar continuar si ya está inicializado o en headless
            pass
        try:
            pygame.mixer.set_num_channels(self._max_channels)
        except Exception:
            pass

    # --- Música ---
    def play_music(self, path: str, loop: bool = True, volume: Optional[float] = None, fade_in_ms: int = 0) -> None:
        try:
            pygame.mixer.music.load(path)
            if volume is not None:
                self._music_volume = float(max(0.0, min(1.0, volume)))
            pygame.mixer.music.set_volume(self._music_volume)
            loops = -1 if loop else 0
            if fade_in_ms > 0:
                pygame.mixer.music.play(loops, fade_ms=int(fade_in_ms))
            else:
                pygame.mixer.music.play(loops)
        except Exception:
            pass

    def stop_music(self, fade_ms: int = 300) -> None:
        try:
            if fade_ms > 0:
                pygame.mixer.music.fadeout(int(fade_ms))
            else:
                pygame.mixer.music.stop()
        except Exception:
            pass

    def set_music_volume(self, v: float) -> None:
        self._music_volume = float(max(0.0, min(1.0, v)))
        try:
            pygame.mixer.music.set_volume(self._music_volume)
        except Exception:
            pass

    # --- SFX ---
    def load_sfx(self, path: str) -> Optional[pygame.mixer.Sound]:
        try:
            snd = pygame.mixer.Sound(path)
            return snd
        except Exception:
            return None

    def play_sfx(self, snd: pygame.mixer.Sound, *, volume: Optional[float] = None, pan: Optional[float] = None,
                 group: str = 'sfx') -> None:
        try:
            ch = pygame.mixer.find_channel()
            if ch is None:
                # Reusar el 0 si no hay libre
                ch = pygame.mixer.Channel(0)
            v = self._sfx_volume if volume is None else float(max(0.0, min(1.0, volume)))
            if pan is None:
                ch.set_volume(v)
            else:
                # Paneo sencillo izq-der
                p = max(-1.0, min(1.0, float(pan)))
                left = v * (1.0 - max(0.0, p))
                right = v * (1.0 + min(0.0, p))
                ch.set_volume(left, right)
            ch.play(snd)
        except Exception:
            pass

    def set_sfx_volume(self, v: float) -> None:
        self._sfx_volume = float(max(0.0, min(1.0, v)))
        try:
            n = pygame.mixer.get_num_channels()
            for i in range(n):
                ch = pygame.mixer.Channel(i)
                ch.set_volume(self._sfx_volume)
        except Exception:
            pass

    def set_ambient_volume(self, v: float) -> None:
        self._ambient_volume = float(max(0.0, min(1.0, v)))
        # Si defines canales dedicados a ambiente, ajústalos aquí.
        # Placeholder: no-op más allá de guardar valor.
        return
