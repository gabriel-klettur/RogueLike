# Path: src/roguelike_engine/config_tiles.py
from roguelike_engine.config_map import MAP_OVERLAYS_DIR

#!------------------------ TILES CONFIG -------------------------
# Tamaño de cada tile en píxeles
TILE_SIZE = 64

# Alias para compatibilidad con módulos de overlay (tile loader, overlay_manager)
TILES_DATA_PATH = MAP_OVERLAYS_DIR

#!------------------------ TILES MAPPING --------------------------

"""
!Mapeo de códigos de overlay de 3 caracteres a nombres de archivos de tiles.
!Cada código → nombre base de asset (sin extensión).
"""

OVERLAY_CODE_MAP: dict[str, str] = {
    # Dungeon variants
    "dn1": "dungeon_1",
    "dn2": "dungeon_2",
    "dn3": "dungeon_3",
    "dc1": "dungeon_c_1",
    "dc2": "dungeon_c_2",

    # Floor variants
    "fl0": "floor",
    "fl1": "floor_1",
    "fl2": "floor_2",
    "fl3": "floor_3",
    "fl4": "floor_4",
    "fl5": "floor_5",
    "fl6": "floor_6",
    "fl7": "floor_7",

    # Wall
    "wal": "wall",
}

# Mapeo inverso: nombre base → lista de códigos (útil para el TilePicker)
INVERSE_OVERLAY_MAP: dict[str, list[str]] = {}
for code, name in OVERLAY_CODE_MAP.items():
    INVERSE_OVERLAY_MAP.setdefault(name, []).append(code)

# Mapeo por defecto para tiles base (solo caracteres simples, si fallara el código)
DEFAULT_TILE_MAP: dict[str, str] = {
    "#": "wall",
    ".": "floor",
    "=": "dungeon_c_1",  # fallback razonable para túneles
    "O": "dungeon_1",    # fallback para habitaciones
}