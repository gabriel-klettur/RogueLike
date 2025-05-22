# Path: src/roguelike_game/systems/editor/tiles/controller/tools/tile_toolbar_controller.py
import pygame
from roguelike_engine.utils.loader import load_image

from roguelike_game.systems.editor.tiles.tiles_editor_config import ICON_PATHS_TILE_TOOLBAR

class TileToolbarController:
    """
    Barra de herramientas para el TileEditorController:
      - select
      - brush
      - eyedropper
      - view
    """

    def __init__(self, editor_state):        
        self.editor = editor_state

        # Cargar iconos (64×64)
        self.icons = {
            tool: load_image(path, (64, 64))
            for tool, path in ICON_PATHS_TILE_TOOLBAR.items()
        }

        # Layout
        self.x = 10
        self.y = 10
        self.size = 64
        self.padding = 8

        # Rects para detectar clicks
        self.icon_rects: dict[str, pygame.Rect] = {}
        # Rects for layer dropdown items
        self.layer_option_rects: dict = {}


    def handle_click(self, mouse_pos) -> bool:
        # Handle layer visibility dropdown clicks
        if self.editor.layers_view_open:
            for layer, rect in self.layer_option_rects.items():
                if rect.collidepoint(mouse_pos):
                    # Toggle layer visibility
                    self.editor.visible_layers[layer] = not self.editor.visible_layers[layer]
                    return True
        for tool, rect in self.icon_rects.items():
            if rect.collidepoint(mouse_pos):                
                if tool == "view":
                    # Toggle main view panel
                    self.editor.view_active = not self.editor.view_active
                elif tool == "view_layers":
                    # Toggle layers visibility dropdown
                    self.editor.layers_view_open = not self.editor.layers_view_open
                else:
                    self.editor.current_tool = tool
                    # Al seleccionar la brocha, abrimos la paleta
                if tool == "brush":
                    self.editor.picker_state.open = True
                    self.editor.scroll_offset    = 0
                return True
        return False