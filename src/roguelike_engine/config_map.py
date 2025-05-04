# Path: src/roguelike_engine/config_map.py
from pathlib import Path
from roguelike_engine.config import DATA_DIR

#!--------------------- GLOBAL MAP CONFIG --------------------------
# Size total del mapa (en tiles)
GLOBAL_WIDTH  = 150
GLOBAL_HEIGHT = 150

#!------------------------ OVERLAY CONFIG --------------------------
# Persistencia de overlays de mapas (tiles)
MAP_OVERLAYS_DIR = Path(DATA_DIR) / "map_overlays"
MAP_OVERLAYS_DIR = str(MAP_OVERLAYS_DIR)

#! --------------------- LOBBY CONFIG ------------------------------

# Offset recomendado para el lobby
LOBBY_OFFSET_X = 0
LOBBY_OFFSET_Y = 0

# Dimensiones lógicas del lobby
LOBBY_WIDTH = 50
LOBBY_HEIGHT = 50

#!------------------------ DUNGEON CONFIG ---------------------------
DUNGEON_CONNECT_SIDE = "bottom"  # nuevo: "top" | "bottom" | "left" | "right"

# Ancho de los túneles (en tiles)
DUNGEON_TUNNEL_THICKNESS = 2

# Dónde situar la dungeon dentro del mundo
DUNGEON_OFFSET_X = 0
DUNGEON_OFFSET_Y = 50

# Dimensiones del dungeon procedural
DUNGEON_WIDTH = 50
DUNGEON_HEIGHT = 50

#!----------------------- DEBUG MAP CONFIG --------------------------

# Debug maps (mapas de debug generados por map_exporter)
DEBUG_MAPS_DIR = Path(DATA_DIR) / "debug_maps"
