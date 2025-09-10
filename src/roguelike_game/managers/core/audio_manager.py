import pygame
from typing import Optional

class AudioManager:
    """
    Gestor simple de audio para volúmenes globales. Por ahora:
    - Música: usa pygame.mixer.music.set_volume
    - SFX: aplica volumen a canales activos conocidos (mejor esfuerzo)
    - Ambiente: reservado para futuras pistas/loops por canal

    Nota: Idealmente los sistemas de juego usarían este manager para reproducir
    sonidos en lugar de usar pygame.mixer.Sound directamente, pero este gestor
    también intenta aplicar el volumen SFX global a los canales existentes.
    """
    def __init__(self, audio_config: Optional[object] = None):
        self.music_volume: float = 0.6
        self.ambient_volume: float = 0.6
        self.sfx_volume: float = 0.7
        if audio_config is not None:
            self.apply_from_config(audio_config)
        else:
            self._apply_all()

    def apply_from_config(self, audio_config) -> None:
        try:
            self.music_volume = float(audio_config.get('music'))
            self.ambient_volume = float(audio_config.get('ambient'))
            self.sfx_volume = float(audio_config.get('sfx'))
        except Exception:
            pass
        self._apply_all()

    def _apply_all(self) -> None:
        self.set_music_volume(self.music_volume)
        self.set_sfx_volume(self.sfx_volume)
        # Ambient reservado para futuro (canales dedicados)

    def set_music_volume(self, v: float) -> None:
        self.music_volume = max(0.0, min(1.0, float(v)))
        try:
            pygame.mixer.music.set_volume(self.music_volume)
        except Exception:
            pass

    def set_sfx_volume(self, v: float) -> None:
        self.sfx_volume = max(0.0, min(1.0, float(v)))
        # Intento de aplicar a todos los canales actuales
        try:
            n = pygame.mixer.get_num_channels()
            for i in range(n):
                ch = pygame.mixer.Channel(i)
                ch.set_volume(self.sfx_volume)
        except Exception:
            pass

    def set_ambient_volume(self, v: float) -> None:
        self.ambient_volume = max(0.0, min(1.0, float(v)))
        # Reservado: si se usa un canal dedicado a ambiente, aplicarlo aquí
        # (p.ej., pygame.mixer.Channel(AMBIENT_CH).set_volume(self.ambient_volume))
        pass
