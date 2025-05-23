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
