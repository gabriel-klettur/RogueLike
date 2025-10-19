import random
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE, OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
from roguelike_engine.utils.loader import load_image

import logging
logger = logging.getLogger(__name__)

# Extremely chatty tile-level logs are disabled by default; enable for deep debugging only
DEBUG_TILES: bool = False

# Caché de imágenes para evitar recargas constantes desde disco
_BASE_TILE_IMAGES_CACHE: dict[str, list[pygame.Surface] | pygame.Surface] | None = None

# Caché de sprites para evitar recomputar o randomizar cada tile
_SPRITE_CACHE: dict[tuple[str, str|None], pygame.Surface | None] = {}


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
    con cache para evitar recomputar/randomizar múltiples veces.
    """
    # Intentar cache
    key = (char, overlay_code)
    if key in _SPRITE_CACHE:
        return _SPRITE_CACHE[key]

    sprite: pygame.Surface | None = None
    if DEBUG_TILES:
        logger.debug(f" get_sprite_for_tile called with char={char!r}, overlay_code={overlay_code!r}")

    # 1) Si hay código de overlay
    if overlay_code:
        name = OVERLAY_CODE_MAP.get(overlay_code)
        if DEBUG_TILES:
            if name:
                logger.debug(f" overlay_code {overlay_code!r} mapped to asset {name!r}")
            else:
                logger.debug(f" overlay_code {overlay_code!r} NOT in OVERLAY_CODE_MAP")

        if name:
            sprite = load_image(f"tiles/{name}.png", (TILE_SIZE, TILE_SIZE))

    if sprite is None:
        base_images = load_base_tile_images()
        imgs = base_images.get(char)
        if imgs is None:
            variant = DEFAULT_TILE_MAP.get(char)
            if variant:
                sprite = load_image(f"tiles/{variant}.png", (TILE_SIZE, TILE_SIZE))
        else:
            sprite = random.choice(imgs) if isinstance(imgs, list) else imgs

    # Guardar en cache y devolver
    _SPRITE_CACHE[key] = sprite
    return sprite