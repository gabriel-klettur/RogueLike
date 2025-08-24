import pygame
from roguelike_engine.utils.loader import load_image

class IconCache:
    """
    Cache singleton para cargar y escalar iconos de ítems.
    """
    _cache: dict[str, pygame.Surface] = {}

    @classmethod
    def get_icon(cls, path: str, size: tuple[int, int]) -> pygame.Surface | None:
        if path in cls._cache:
            return cls._cache[path]
        try:
            # Usar el cargador centralizado que normaliza rutas con o sin prefijo "assets/"
            img = load_image(path, size)
            cls._cache[path] = img
            return img
        except Exception:
            return None
