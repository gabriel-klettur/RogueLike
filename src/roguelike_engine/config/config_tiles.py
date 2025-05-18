# Path: src/roguelike_engine/config/config_tiles.py

import os
import json
from pathlib import Path

from roguelike_engine.config.config import ASSETS_DIR, DATA_DIR

#!------------------------ TILES CONFIG -------------------------
# Tamaño de cada tile en píxeles
TILE_SIZE = 32

# Carpeta donde se almacenan sprites y datos de tiles personalizados
TILES_DATA_PATH = Path(ASSETS_DIR) / "tiles"

# Carpeta donde se almacenan los overlays generados (JSON)
MAP_OVERLAYS_DIR = Path(DATA_DIR) / "map_overlays"
os.makedirs(MAP_OVERLAYS_DIR, exist_ok=True)

#!------------------------ TILES MAPPING --------------------------
# Mapeo dinámico: código de overlay → nombre base de asset
# Generado automáticamente por scripts/generate_overlay_map.py

overlay_map_path = MAP_OVERLAYS_DIR / "overlay_map.json"
if overlay_map_path.is_file():
    with open(overlay_map_path, "r", encoding="utf-8") as f:
        OVERLAY_CODE_MAP = json.load(f)
else:
    OVERLAY_CODE_MAP = {}

INVERSE_OVERLAY_MAP: dict[str, list[str]] = {}
for code, name in OVERLAY_CODE_MAP.items():
    INVERSE_OVERLAY_MAP.setdefault(name, []).append(code)

# Fallback para caracteres simples si faltara overlay
DEFAULT_TILE_MAP: dict[str, str] = {
    "#": "wall",
    ".": "floor",
    "=": "dungeon_c_1",
    "O": "dungeon_1",
}

TILE_COLORS: dict[str, tuple[int,int,int]] = {
    ".": (80, 80, 80),
    "O": (130, 130, 130),
    "=": (100, 100, 100),
    "#": (30, 30, 30),
    "D": (90, 90, 90),
}
