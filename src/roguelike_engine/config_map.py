# Path: src/roguelike_engine/config_map.py
from pathlib import Path
from roguelike_engine.config import DATA_DIR

#! --------------------- LOBBY CONFIG ------------------------------

# Offset recomendado para el lobby
LOBBY_OFFSET_X = 5
LOBBY_OFFSET_Y = 5

# Dimensiones lógicas del lobby
LOBBY_WIDTH = 100
LOBBY_HEIGHT = 100

#!------------------------ OVERLAY CONFIG --------------------------
# Persistencia de overlays de mapas (tiles)
MAP_OVERLAYS_DIR = Path(DATA_DIR) / "map_overlays"
MAP_OVERLAYS_DIR = str(MAP_OVERLAYS_DIR)


#!------------------------ DUNGEON CONFIG ---------------------------
# Dimensiones del dungeon procedural
DUNGEON_WIDTH = 120
DUNGEON_HEIGHT = 120


# Debug maps (mapas de debug generados por map_exporter)
DEBUG_MAPS_DIR = Path(DATA_DIR) / "debug_maps"


