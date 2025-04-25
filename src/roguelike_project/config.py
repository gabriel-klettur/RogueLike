import os
# src.roguelike_project/config.py

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

#!------------------------ PATH CONFIG --------------------------

# Ruta al paquete roguelike_project (el que contiene /assets, /engine, /utils, etc.)
PACKAGE_ROOT = os.path.dirname(__file__)

# Carpeta de recursos
ASSETS_DIR = os.path.join(PACKAGE_ROOT, "assets")

#!------------------------ Z-LAYER CONFIG --------------------------

# Z-Layer
DEFAULT_Z = 1

# Z-Layer buildings data
BUILDINGS_DATA_PATH = "src.roguelike_project/systems/z_layer/data/buildings_data.json"

#!--------------------- MAP CONFIG --------------------------

# Tamaño de tile
TILE_SIZE = 64

# Tamaño del mapa procedural
DUNGEON_WIDTH = 120
DUNGEON_HEIGHT = 120

# Tamaño lógico del lobby insertado
LOBBY_WIDTH = 100   # (basado en la cantidad de caracteres por línea)
LOBBY_HEIGHT = 100  # (número de líneas)

# Offset visual recomendado
LOBBY_OFFSET_X = 5
LOBBY_OFFSET_Y = 5