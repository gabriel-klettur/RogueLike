from pathlib import Path

#! ------------------------ MAIN GAME SETTINGS ------------------------

# Número máximo de rectángulos "sucios" (dirty rects) permitidos antes de forzar un repintado completo.
MAX_DIRTY = 50

# Debug Mode
DEBUG = False
DEBUG_HITBOX = False
DEBUG_ENTITIES = False  # FSM Editor (FSM, IA, etc.)
DEBUG_ENTITIES_FRAME_SKIP = 2  # dibuja el overlay del FSM Editor cada N frames

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

# Carpeta donde se almacenan datos persistentes 
DATA_DIR = PROJECT_ROOT / "data"
DATA_DIR = str(DATA_DIR)


#!------------------------ BUILDINGS CONFIG ------------------------
# Persistencia de edificios (modo split únicamente)

# Persistencia de colisiones de edificios
BUILDINGS_COLLISIONS_DATA_PATH = Path(DATA_DIR) / "buildings" / "buildings_collisions_data.json"
BUILDINGS_COLLISIONS_DATA_PATH = str(BUILDINGS_COLLISIONS_DATA_PATH)

# Nuevos archivos divididos para colisiones de edificios
# - Globales por image_path (CG)
BUILDINGS_COLLISIONS_BY_IMAGE_PATH = Path(DATA_DIR) / "buildings" / "buildings_collisions_by_image.json"
BUILDINGS_COLLISIONS_BY_IMAGE_PATH = str(BUILDINGS_COLLISIONS_BY_IMAGE_PATH)
# - Legacy por spawn_id (soportado en lectura/escritura)
BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = Path(DATA_DIR) / "buildings" / "buildings_collisions_by_spawn_id.json"
BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = str(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH)
# - Por instancia de edificio (CU)
BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = Path(DATA_DIR) / "buildings" / "buildings_collisions_by_building_instance_id.json"
BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = str(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH)

# Rutas nuevas para la separación Templates/Instances de Buildings
# Canon: el sistema usa SIEMPRE estos archivos (sin fallback legacy)
BUILDINGS_TEMPLATES_PATH = Path(DATA_DIR) / "buildings" / "buildings_templates.json"
BUILDINGS_TEMPLATES_PATH = str(BUILDINGS_TEMPLATES_PATH)

BUILDINGS_INSTANCES_PATH = Path(DATA_DIR) / "buildings" / "buildings_instances.json"
BUILDINGS_INSTANCES_PATH = str(BUILDINGS_INSTANCES_PATH)

#!------------------------ PARTICLES CONFIG ------------------------
# Persistencia de instancias de partículas colocadas en el mapa
PARTICLES_INSTANCES_PATH = Path(DATA_DIR) / "particles" / "particles_instances.json"
PARTICLES_INSTANCES_PATH = str(PARTICLES_INSTANCES_PATH)


#! ------------------------ DEV/TOOLS FLAGS -----------------------
# Auto-importar nuevas imágenes de assets/buildings como plantillas al iniciar (solo DEV)
# TODO: deshabilitar en producción
# TODO Deberia estar en True cada vez que agregamos nuevas imagenes a nuestros buildings
DEV_AUTO_IMPORT_BUILDINGS = True
# Patrones a excluir (fnmatch) al escanear assets/buildings
DEV_AUTO_IMPORT_EXCLUDES = [
    "**/WIP/**",
    "**/_wip/**",
    "**/tmp/**",
    "**/*.aseprite",
]
# Crear instancias placeholder automáticamente para nuevas plantillas (no recomendado por defecto)
DEV_AUTO_IMPORT_CREATE_INSTANCES = False
# Zona y posición por defecto si se crean instancias automáticamente
DEV_AUTO_IMPORT_DEFAULT_ZONE = "no zone"
DEV_AUTO_IMPORT_DEFAULT_REL_POS = (0, 0)


#! ------------------------ Z-LAYER CONFIG -----------------------
DEFAULT_Z = 1

#! ------------------------ SERVER CONFIG ------------------------
# WebSocket URL
WEBSOCKET_URL = "ws://localhost:8000/ws"