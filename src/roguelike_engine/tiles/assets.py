
# Path: src/roguelike_engine/tiles/assets.py
import os
import random
import pygame

from roguelike_engine.config import ASSETS_DIR
from roguelike_engine.config_tiles import TILE_SIZE, OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
from roguelike_engine.utils.loader import load_image

# Caché de imágenes para evitar recargas constantes desde disco
_BASE_TILE_IMAGES_CACHE: dict[str, list[pygame.Surface] | pygame.Surface] | None = None


def load_base_tile_images(theme: str = "default") -> dict[str, list[pygame.Surface] | pygame.Surface]:
    """
    Carga y devuelve el mapeo base de caracteres a sprites o listas de variantes.
    Se almacena en caché tras la primera invocación para evitar lecturas repetidas.
    """
    global _BASE_TILE_IMAGES_CACHE
    if _BASE_TILE_IMAGES_CACHE is not None:
        return _BASE_TILE_IMAGES_CACHE

    # Directorio de assets/tiles (no se usa aquí directamente, pero podría servir para expansiones)
    # tiles_dir = os.path.join(ASSETS_DIR, "tiles")

    # Variantes de suelo
    floor_variants = [
        load_image(f"tiles/floor_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 8)
    ]
    # Variantes de dungeon y túneles
    dungeon_variants = [
        load_image(f"tiles/dungeon_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 2)
    ]
    tunnel_variants = [
        load_image(f"tiles/dungeon_c_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 2)
    ]

    base_map: dict[str, list[pygame.Surface] | pygame.Surface] = {
        ".": floor_variants,
        "#": load_image("tiles/wall.png", (TILE_SIZE, TILE_SIZE)),
        "D": dungeon_variants,
        "O": dungeon_variants,
        "=": tunnel_variants,
    }

    _BASE_TILE_IMAGES_CACHE = base_map
    return base_map


def get_sprite_for_tile(char: str, overlay_code: str | None = None) -> pygame.Surface | None:
    """
    Determina y devuelve el sprite para un carácter de mapa y código de overlay opcional.
    - Primero prioriza cualquier código de overlay.
    - Luego toma una variante aleatoria del mapa base (o la imagen única).
    - Si no encuentra nada, recurre a DEFAULT_TILE_MAP.
    """
    # 1) Si hay código de overlay, priorizar ese asset
    if overlay_code:
        name = OVERLAY_CODE_MAP.get(overlay_code)
        if name:
            return load_image(f"tiles/{name}.png", (TILE_SIZE, TILE_SIZE))

    # 2) Usar el mapa base cacheado
    base_images = load_base_tile_images()
    imgs = base_images.get(char)
    if imgs is None:
        # 3) Fallback a DEFAULT_TILE_MAP
        variant = DEFAULT_TILE_MAP.get(char)
        if variant:
            return load_image(f"tiles/{variant}.png", (TILE_SIZE, TILE_SIZE))
        return None

    # 4) Si es lista de variantes, elegir una al azar
    if isinstance(imgs, list):
        return random.choice(imgs)
    return imgs