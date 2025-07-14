import pygame, os

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
            raw = pygame.image.load(os.path.join(os.getcwd(), path)).convert_alpha()
            img = pygame.transform.scale(raw, size)
            cls._cache[path] = img
            return img
        except Exception:
            return None
