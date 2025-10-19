OUTLINE_SEL    = (0, 255, 0)     # seleccionado (verde)
OUTLINE_HOVER  = (0, 220, 255)   # hover (cian)
OUTLINE_CHOICE = (255, 255, 0)   # elección actual (amarillo)

THUMB = 56
COLS  = 6
PAD   = 6

CLR_BORDER     = (255, 255, 255)
CLR_HOVER      = (255, 230, 0)
CLR_SELECTION  = (255, 200, 0)

TOOLS = ["select", "brush", "eyedropper", "view", "view_layers", "view_collisions", "delete", "default","tutorial_tiles"]
ICON_PATHS_TILE_TOOLBAR = {    
    "select":           "assets/ui/select_tool.png",
    "brush":            "assets/ui/brush_tool.png",
    "eyedropper":       "assets/ui/eyedropper_tool.png",
    "view":             "assets/ui/view_tool.png",
    "view_layers":      "assets/ui/layers_view_tool.png",
    "view_collisions":  "assets/ui/collision_tool.png",
    "delete":           "assets/ui/delete_icon.png",
    "default":          "assets/ui/default_icon.png",
    "tutorial_tiles":   "assets/ui/tutorials_button.png",
}

BTN_W = 100
BTN_H = 28
BASE_TILE_DIR = "tiles"

# Iconos especiales
ARROW_UP_ICON = "assets/objects/arrow_left.png"
FOLDER_ICON   = "assets/objects/folder_win.png"

# Patrones de ficheros que nos interesan
FILE_PATTERNS = ["*.png", "*.PNG", "*.webp", "*.WEBP"]

# --------- Editor rendering constants (avoid magic numbers) ---------
# Grosor de los rectángulos de contorno en vistas
OUTLINE_WIDTH = 3
# Opacidad del relleno hover (0-255)
HOVER_ALPHA = 60
# Duración del parpadeo del eyedropper en milisegundos
EYEDROPPER_BLINK_DURATION_MS = 3000
# Intervalo del parpadeo del eyedropper en milisegundos
EYEDROPPER_BLINK_INTERVAL_MS = 300
# Throttle para actualizaciones parciales de chunks durante el brush (ms)
BRUSH_UPDATE_THROTTLE_MS = 16
# Path: src/roguelike_game/systems/editor/tiles/tiles_editor_config.py