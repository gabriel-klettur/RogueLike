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
PACKAGE_DIR = Path(__file__).parent

# 2) Raíz del proyecto (…/RogueLike)
PROJECT_ROOT = PACKAGE_DIR.parent.parent

# 3) Carpeta global de assets (…/RogueLike/assets)
ASSETS_DIR = PROJECT_ROOT / "assets"
ASSETS_DIR = str(ASSETS_DIR)

# ------------------------ DATA PATHS ---------------------------

# Carpeta donde se almacenan datos persistentes (edificios, overlays, debug maps)
DATA_DIR = PROJECT_ROOT / "data"
DATA_DIR = str(DATA_DIR)

# Debug maps (mapas de debug generados por map_exporter)
DEBUG_MAPS_DIR = Path(DATA_DIR) / "debug_maps"
DEBUG_MAPS_DIR = str(DEBUG_MAPS_DIR)

# Alias legado para compatibilidad con map_exporter
MAP_DEBUG_DIR = DEBUG_MAPS_DIR

# Persistencia de edificios
BUILDINGS_DATA_PATH = Path(DATA_DIR) / "buildings" / "buildings_data.json"
BUILDINGS_DATA_PATH = str(BUILDINGS_DATA_PATH)

# Persistencia de overlays de mapas (tiles)
MAP_OVERLAYS_DIR = Path(DATA_DIR) / "map_overlays"
MAP_OVERLAYS_DIR = str(MAP_OVERLAYS_DIR)

# Alias para compatibilidad con módulos de overlay (tile loader, overlay_manager)
TILES_DATA_PATH = MAP_OVERLAYS_DIR

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
LOBBY_WIDTH = 100
LOBBY_HEIGHT = 100

# Dimensiones del dungeon procedural
DUNGEON_WIDTH = 120
DUNGEON_HEIGHT = 120
