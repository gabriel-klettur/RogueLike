# Path: src/roguelike_game/systems/editor/tiles/tiles_editor_config.py

OUTLINE_SEL    = (0, 255, 0)     # seleccionado (verde)
OUTLINE_HOVER  = (0, 220, 255)   # hover (cian)
OUTLINE_CHOICE = (255, 255, 0)   # elección actual (amarillo)

THUMB = 56
COLS  = 6
PAD   = 6

CLR_BORDER     = (255, 255, 255)
CLR_HOVER      = (255, 230, 0)
CLR_SELECTION  = (255, 200, 0)

TOOLS = ["select", "brush", "eyedropper", "view"]
ICON_PATHS_TILE_TOOLBAR = {
    "select":     "assets/ui/select_tool.png",
    "brush":      "assets/ui/brush_tool.png",
    "eyedropper": "assets/ui/eyedropper_tool.png",
    "view":       "assets/ui/view_tool.png",
}

BTN_W = 100
BTN_H = 28

# Path base donde buscar
BASE_TILE_DIR = "tiles"

# Iconos especiales
ARROW_UP_ICON = "assets/objects/arrow_left.png"
FOLDER_ICON   = "assets/objects/folder_win.png"

# Patrones de ficheros que nos interesan
FILE_PATTERNS = ["*.png", "*.PNG", "*.webp", "*.WEBP"]