# Path: src/roguelike_engine/config_map.py
from pathlib import Path
from roguelike_engine.config import DATA_DIR

#!--------------------- GLOBAL MAP CONFIG --------------------------
# Size total del mapa (en tiles)
GLOBAL_WIDTH  = 150
GLOBAL_HEIGHT = 150
ZONE_SIZE = (50, 50)
ZONE_WIDTH = ZONE_SIZE[0]
ZONE_HEIGHT = ZONE_SIZE[1]

#!------------------------ OVERLAY CONFIG --------------------------
# Persistencia de overlays de mapas (tiles)
MAP_OVERLAYS_DIR = Path(DATA_DIR) / "map_overlays"
MAP_OVERLAYS_DIR = str(MAP_OVERLAYS_DIR)



#!------------------------ DUNGEON CONFIG ---------------------------
DUNGEON_CONNECT_SIDE = "bottom"  # nuevo: "top" | "bottom" | "left" | "right"

# Ancho de los túneles (en tiles)
DUNGEON_TUNNEL_THICKNESS = 3


# Límite de habitaciones:
#  - None → usar valor dinámico aleatorio (max_rooms pasado al generate)
#  - int  → máximo fijo
#  - 'MAX'→ calcular el máximo teórico según room_min_size
DUNGEON_MAX_ROOMS = '10'

#!----------------------- DEBUG MAP CONFIG --------------------------

# Debug maps (mapas de debug generados por map_exporter)
DEBUG_MAPS_DIR = Path(DATA_DIR) / "debug_maps"

#!----------------------- ZONE OFFSETS -----------------------------
ZONE_OFFSETS = {
    "lobby": (50, 50),
    "dungeon": (50, 100),
    # "city": (ciudad_offset_x, ciudad_offset_y),  → futuras zonas
}