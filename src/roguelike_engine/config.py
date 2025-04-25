# src/roguelike_engine/config.py

import os
from pathlib import Path

# ------------------------ GAME SETTINGS ------------------------

# Debug Mode
DEBUG = True

# Pantalla
SCREEN_WIDTH = 1600
SCREEN_HEIGHT = 800
FPS = 60

# Fuente principal
FONT_NAME = "Arial"
FONT_SIZE = 18

# WebSocket URL
WEBSOCKET_URL = "ws://localhost:8000/ws"


# ------------------------ PATH CONFIG --------------------------

# 1) Directorio de este paquete (…/src/roguelike_engine)
PACKAGE_DIR  = Path(__file__).parent

# 2) Raíz del proyecto (…/RogueLike)
PROJECT_ROOT = PACKAGE_DIR.parent.parent

# 3) Carpeta global de assets (…/RogueLike/assets)
ASSETS_DIR   = PROJECT_ROOT / "assets"
ASSETS_DIR   = str(ASSETS_DIR)  # conviértelo a str si lo necesitas así


# ------------------------ Z-LAYER CONFIG -----------------------

# Z-Layer por defecto
DEFAULT_Z = 1


# --------------------- TILE CONFIG -----------------------------

# Tamaño de cada tile en píxeles
TILE_SIZE = 64

# Offset recomendado para el lobby
LOBBY_OFFSET_X = 5
LOBBY_OFFSET_Y = 5


# --------------------- MAP CONFIG ------------------------------

# Dimensiones lógicas del lobby
LOBBY_WIDTH  = 100
LOBBY_HEIGHT = 100

# Dimensiones del dungeon procedural
DUNGEON_WIDTH  = 120
DUNGEON_HEIGHT = 120

# Carpeta donde guardar mapas de debug:
# …/src/roguelike_engine/map/debug_maps
MAP_DEBUG_DIR = PACKAGE_DIR / "map" / "debug_maps"
MAP_DEBUG_DIR = str(MAP_DEBUG_DIR)

# Carpeta donde guardas/cargas los overlay JSON de tiles:
# …/src/roguelike_engine/map/overlay
TILES_DATA_PATH = PACKAGE_DIR / "map" / "overlay"
TILES_DATA_PATH = str(TILES_DATA_PATH)


# ---------------- BUILDINGS DATA PATH --------------------------

# Ruta al JSON de edificios dentro del juego:
# …/src/roguelike_game/systems/z_layer/data/buildings_data.json
BUILDINGS_DATA_PATH = (
    PROJECT_ROOT
    / "src"
    / "roguelike_game"
    / "systems"
    / "z_layer"
    / "data"
    / "buildings_data.json"
)
BUILDINGS_DATA_PATH = str(BUILDINGS_DATA_PATH)
