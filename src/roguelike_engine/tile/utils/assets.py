import random
import pygame
import json
from pathlib import Path

from roguelike_engine.config.config_tiles import TILE_SIZE, OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.map_config import global_map_settings

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
    sprite: pygame.Surface | None = None
    # Deferred cache lookup until after policy is established
    key = (char, overlay_code)
    if DEBUG_TILES:
        logger.debug(f" get_sprite_for_tile called with char={char!r}, overlay_code={overlay_code!r}")

    # Política overlay-only: detectar mundo en blanco o overlays sentinela sin depender únicamente de is_blank_world
    overlay_only = False
    # 1) Preferir MapSettings.is_blank_world() si existe
    try:
        overlay_only = bool(getattr(global_map_settings, 'is_blank_world', lambda: False)())
    except Exception:
        overlay_only = False
    # 2) Fallback: inspeccionar ZONES_INDEX para ver si no hay zonas de usuario (excluyendo sentinelas)
    if overlay_only is False:
        try:
            z = getattr(global_map_settings, 'ZONES_INDEX', None)
            zones_empty = False
            if z and hasattr(z, 'exists') and z.exists():
                txt = z.read_text(encoding='utf-8').strip()
                if txt:
                    try:
                        data = json.loads(txt)
                        if isinstance(data, dict):
                            user_keys = [k for k in data.keys() if str(k).lower() not in ('no zone', 'no-zone', 'no_zone')]
                            zones_empty = len(user_keys) == 0
                        else:
                            zones_empty = True
                    except Exception:
                        zones_empty = False
                else:
                    zones_empty = True
            else:
                zones_empty = True
            if zones_empty:
                overlay_only = True
        except Exception:
            pass
    # 3) Fallback adicional: si la carpeta de overlays está vacía o solo hay sentinelas, forzar overlay-only
    try:
        odir = getattr(global_map_settings, 'overlays_dir', None)
        files = list(Path(odir).glob('*.overlay.json')) if odir else []
        if not files:
            overlay_only = True
        else:
            stems = {
                (s[:-8] if s.endswith('.overlay') else s)
                for s in (f.stem.lower().replace('_', ' ') for f in files)
            }
            if stems.issubset({'no zone', 'no-zone', 'no_zone'}):
                overlay_only = True
    except Exception:
        pass

    # If overlay-only policy is active and there's no valid overlay code, do not fallback to base sprite
    if overlay_only and (not overlay_code or overlay_code not in OVERLAY_CODE_MAP):
        _SPRITE_CACHE[key] = None
        return None

    # Cache fast path (only after enforcing overlay-only policy)
    cached = _SPRITE_CACHE.get(key, None)
    if cached is not None:
        return cached

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
        # En overlay-only ya se aplicó la política arriba
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


def clear_sprite_caches() -> None:
    """Limpia caches de sprites para evitar artefactos entre mundos."""
    try:
        _SPRITE_CACHE.clear()
    except Exception:
        pass
    # Reiniciar cache de imágenes base para permitir re-carga si fuera necesario
    try:
        global _BASE_TILE_IMAGES_CACHE
        _BASE_TILE_IMAGES_CACHE = None
    except Exception:
        pass