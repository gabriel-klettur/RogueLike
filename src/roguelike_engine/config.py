# Path: src/roguelike_engine/config.py
from pathlib import Path

#! ------------------------ MAIN GAME SETTINGS ------------------------

# Debug Mode
DEBUG = True

# Pantalla
SCREEN_WIDTH = 1600
SCREEN_HEIGHT = 800
FPS = 60

# Fuente principal
FONT_NAME = "Arial"
FONT_SIZE = 18

#! ------------------------ PATH CONFIG --------------------------

# 1) Directorio de este paquete (…/src/roguelike_engine)
PACKAGE_DIR = Path(__file__).parent

# 2) Raíz del proyecto (…/RogueLike)
PROJECT_ROOT = PACKAGE_DIR.parent.parent

# 3) Carpeta global de assets (…/RogueLike/assets)
ASSETS_DIR = PROJECT_ROOT / "assets"
ASSETS_DIR = str(ASSETS_DIR)

# Carpeta donde se almacenan datos persistentes 
DATA_DIR = PROJECT_ROOT / "data"
DATA_DIR = str(DATA_DIR)


#!------------------------ BUILDINGS CONFIG ------------------------
# Persistencia de edificios
BUILDINGS_DATA_PATH = Path(DATA_DIR) / "buildings" / "buildings_data.json"
BUILDINGS_DATA_PATH = str(BUILDINGS_DATA_PATH)


#! ------------------------ Z-LAYER CONFIG -----------------------
DEFAULT_Z = 1

#! ------------------------ SERVER CONFIG ------------------------
# WebSocket URL
WEBSOCKET_URL = "ws://localhost:8000/ws"