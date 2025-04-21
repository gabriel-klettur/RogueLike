# roguelike_project/systems/editor/tiles/toolbar.py

import pygame
from roguelike_project.utils.loader import load_image

class TileToolbar:
    """
    Barra de herramientas para el TileEditorController:
      - select
      - brush
      - eyedropper
      - view
    """
    TOOLS = ["select", "brush", "eyedropper", "view"]
    ICON_PATHS = {
        "select":     "assets/ui/select_tool.png",
        "brush":      "assets/ui/brush_tool.png",
        "eyedropper": "assets/ui/eyedropper_tool.png",
        "view":       "assets/ui/view_tool.png",
    }

    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

        # Cargar iconos (64×64)
        self.icons = {
            tool: load_image(path, (64, 64))
            for tool, path in self.ICON_PATHS.items()
        }

        # Layout
        self.x = 10
        self.y = 10
        self.size = 64
        self.padding = 8

        # Rects para detectar clicks
        self.icon_rects: dict[str, pygame.Rect] = {}

 

    def handle_click(self, mouse_pos) -> bool:
        for tool, rect in self.icon_rects.items():
            if rect.collidepoint(mouse_pos):                
                if tool == "view":
                    # Toggle view panel
                    self.editor.view_active = not self.editor.view_active
                else:
                    self.editor.current_tool = tool
                    # Al seleccionar la brocha, abrimos la paleta
                    if tool == "brush":
                        self.editor.picker_open = True
                        self.editor.scroll_offset = 0
                return True
        return False
