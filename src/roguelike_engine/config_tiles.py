# Path: src/roguelike_engine/config_tiles.py
from roguelike_engine.config_map import MAP_OVERLAYS_DIR
from data.map_overlays.overlay_map import OVERLAY_CODE_MAP

#!------------------------ TILES CONFIG -------------------------
# Tamaño de cada tile en píxeles
TILE_SIZE = 32

# Carpeta donde se almacenan datos de overlay
TILES_DATA_PATH = MAP_OVERLAYS_DIR

#!------------------------ TILES MAPPING --------------------------
# Mapeo dinámico: código de overlay → nombre base de asset
# Generado automáticamente por scripts/generate_overlay_map.py
INVERSE_OVERLAY_MAP: dict[str, list[str]] = {}
for code, name in OVERLAY_CODE_MAP.items():
    INVERSE_OVERLAY_MAP.setdefault(name, []).append(code)

# Fallback para caracteres simples si faltara overlay
DEFAULT_TILE_MAP: dict[str, str] = {
    "#": "wall",
    ".": "floor",
    "=": "dungeon_c_1",  # fallback razonable para túneles
    "O": "dungeon_1",    # fallback para habitaciones
}

TILE_COLORS: dict[str, tuple[int,int,int]] = {
    ".": (80, 80, 80),    # piso
    "O": (130, 130, 130), # sala
    "=": (100, 100, 100), # túnel
    "#": (30, 30, 30),    # muro
    "D": (90, 90, 90),    # dungeon genérico
}