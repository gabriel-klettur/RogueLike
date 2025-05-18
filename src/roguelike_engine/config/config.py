# Path: src/roguelike_engine/config.py
from pathlib import Path

#! ------------------------ MAIN GAME SETTINGS ------------------------

# Número máximo de rectángulos "sucios" (dirty rects) permitidos antes de forzar un repintado completo.
MAX_DIRTY = 50

# Debug Mode
DEBUG = False

# Pantalla
SCREEN_WIDTH = 1600
SCREEN_HEIGHT = 800
FPS = 60

# Fuente principal
FONT_NAME = "Arial"
FONT_SIZE = 18

#! ------------------------ PATH CONFIG --------------------------

# 1) Directorio de este paquete (…/src/roguelike_engine)
ENGINE_DIR = Path(__file__).parent
# 2) Raíz del proyecto (…/RogueLike)
PROJECT_ROOT = ENGINE_DIR.parent.parent.parent

# 3) Carpeta global de assets (…/RogueLike/assets)
ASSETS_DIR = PROJECT_ROOT / "assets"
ASSETS_DIR = str(ASSETS_DIR)
print(f"[DEBUG] ASSETS_DIR = {ASSETS_DIR!r}")

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