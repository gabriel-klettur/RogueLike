# Path: src/roguelike_engine/config_map.py
from pathlib import Path
from roguelike_engine.config import DATA_DIR

#!--------------------- GLOBAL MAP CONFIG --------------------------
# Size total del mapa (en tiles)
GLOBAL_WIDTH  = 200
GLOBAL_HEIGHT = 200

#!------------------------ OVERLAY CONFIG --------------------------
# Persistencia de overlays de mapas (tiles)
MAP_OVERLAYS_DIR = Path(DATA_DIR) / "map_overlays"
MAP_OVERLAYS_DIR = str(MAP_OVERLAYS_DIR)

#! --------------------- LOBBY CONFIG ------------------------------

# Offset recomendado para el lobby
LOBBY_OFFSET_X = 1
LOBBY_OFFSET_Y = 1

# Dimensiones lógicas del lobby
LOBBY_WIDTH = 120
LOBBY_HEIGHT = 120

#!------------------------ DUNGEON CONFIG ---------------------------
# Dónde situar la dungeon dentro del mundo
DUNGEON_OFFSET_X = 20
DUNGEON_OFFSET_Y = 20

# Dimensiones del dungeon procedural
DUNGEON_WIDTH = 100
DUNGEON_HEIGHT = 100

#!----------------------- DEBUG MAP CONFIG --------------------------

# Debug maps (mapas de debug generados por map_exporter)
DEBUG_MAPS_DIR = Path(DATA_DIR) / "debug_maps"
