import pygame
from roguelike_engine.utils.loader import load_image

from roguelike_editors.tiles.tiles_editor_config import ICON_PATHS_TILE_TOOLBAR

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


    def select_tile(self, choice_path):
        """
        Selecciona un tile y cambia herramienta a brush.
        """
        self.editor.current_choice = choice_path
        self.editor.current_tool = "brush"