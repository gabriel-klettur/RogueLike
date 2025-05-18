# Path: src/roguelike_engine/map_config.py
from pathlib import Path
from roguelike_engine.config.config import DATA_DIR

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
#!--------------------- ZONE OFFSETS (DINÁMICOS) -------------------

def _compute_lobby_offset():
    # ¿Cuántas zonas caben horizontal/verticalmente?
    n_cols = GLOBAL_WIDTH  // ZONE_WIDTH
    n_rows = GLOBAL_HEIGHT // ZONE_HEIGHT
    if n_cols < 1 or n_rows < 1:
        # Si el mapa es más pequeño que una zona
        return ((GLOBAL_WIDTH - ZONE_WIDTH) // 2,
                (GLOBAL_HEIGHT - ZONE_HEIGHT) // 2)
    # Centro de la cuadrícula de zonas
    center_col = n_cols // 2
    center_row = n_rows // 2
    # Espacios remanentes
    rem_x = GLOBAL_WIDTH  - n_cols * ZONE_WIDTH
    rem_y = GLOBAL_HEIGHT - n_rows * ZONE_HEIGHT
    start_x = rem_x // 2
    start_y = rem_y // 2
    return (
        start_x + center_col * ZONE_WIDTH,
        start_y + center_row * ZONE_HEIGHT
    )

_LOBBY_OFFSET = _compute_lobby_offset()


ZONE_OFFSETS = {
    "lobby":   _LOBBY_OFFSET    
}