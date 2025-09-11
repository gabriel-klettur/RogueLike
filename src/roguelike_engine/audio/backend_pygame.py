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
        # Índices de canales activos por grupo y mapeo inverso canal->grupo
        self._channels_per_group: Dict[str, list[int]] = {
            'sfx': [], 'ui': [], 'combat': [], 'ambient': []
        }
        self._channel_group_map: Dict[int, str] = {}
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

    def prepare_music(self, path: str) -> None:
        """Carga el archivo de música sin iniciarla (preparación anticipada)."""
        try:
            pygame.mixer.music.load(path)
        except Exception:
            pass

    def play_prepared_music(self, loop: bool = True, volume: Optional[float] = None, fade_in_ms: int = 0) -> None:
        """Reproduce la música previamente cargada con prepare_music."""
        try:
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
        """Reproduce un SFX respetando el volumen base del grupo.
        Groups conocidos: 'sfx' (incluye ui/combat) y 'ambient'.
        """
        try:
            # Elegir canal manualmente para conocer su índice
            try:
                n = pygame.mixer.get_num_channels()
            except Exception:
                n = self._max_channels
            idx = None
            for i in range(n):
                c = self._channels.get(i) or pygame.mixer.Channel(i)
                self._channels[i] = c
                busy = False
                try:
                    busy = bool(c.get_busy())
                except Exception:
                    busy = False
                if not busy:
                    idx = i
                    ch = c
                    break
            if idx is None:
                # Reusar el 0 si no hay libre
                idx = 0
                ch = self._channels.get(0) or pygame.mixer.Channel(0)
                self._channels[0] = ch
            # Resolver volumen base por grupo
            g = str(group or 'sfx').lower()
            if g == 'ambient':
                base = self._ambient_volume
            else:
                # Tratar 'ui' y 'combat' como SFX generales
                base = self._sfx_volume
                if g not in ('sfx', 'ui', 'combat'):
                    g = 'sfx'
            # El parámetro volume (si viene) actúa como factor multiplicativo sobre el grupo
            fac = 1.0 if volume is None else float(max(0.0, min(1.0, volume)))
            v = max(0.0, min(1.0, base * fac))
            if pan is None:
                ch.set_volume(v)
            else:
                # Paneo sencillo izq-der
                p = max(-1.0, min(1.0, float(pan)))
                left = v * (1.0 - max(0.0, p))
                right = v * (1.0 + min(0.0, p))
                ch.set_volume(left, right)
            ch.play(snd)
            # Registrar el canal bajo su grupo (ya conocemos idx)
            if isinstance(idx, int):
                # Quitar de su grupo previo si existía
                prev = self._channel_group_map.get(idx)
                if prev and prev in self._channels_per_group:
                    try:
                        self._channels_per_group[prev] = [j for j in self._channels_per_group[prev] if j != idx]
                    except Exception:
                        pass
                # Registrar en nuevo grupo
                self._channel_group_map[idx] = g
                lst = self._channels_per_group.setdefault(g, [])
                if idx not in lst:
                    lst.append(idx)
        except Exception:
            pass

    def set_sfx_volume(self, v: float) -> None:
        self._sfx_volume = float(max(0.0, min(1.0, v)))
        # Aplicar a canales de grupos sfx/ui/combat
        try:
            groups = ('sfx', 'ui', 'combat')
            for g in groups:
                for idx in list(self._channels_per_group.get(g, [])):
                    try:
                        ch = self._channels.get(idx) or pygame.mixer.Channel(idx)
                        ch.set_volume(self._sfx_volume)
                        self._channels[idx] = ch
                    except Exception:
                        pass
        except Exception:
            pass

    def set_ambient_volume(self, v: float) -> None:
        self._ambient_volume = float(max(0.0, min(1.0, v)))
        # Aplicar solo a los canales registrados como 'ambient'
        try:
            for idx in list(self._channels_per_group.get('ambient', [])):
                try:
                    ch = self._channels.get(idx) or pygame.mixer.Channel(idx)
                    ch.set_volume(self._ambient_volume)
                    self._channels[idx] = ch
                except Exception:
                    pass
        except Exception:
            pass
