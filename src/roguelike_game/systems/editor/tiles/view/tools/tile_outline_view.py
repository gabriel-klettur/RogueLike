# roguelike_project/systems/editor/tiles/view/tools/tile_outline_view.py

# Path: src/roguelike_game/systems/editor/tiles/view/tools/tile_outline_view.py
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.systems.editor.tiles.tiles_editor_config import OUTLINE_HOVER, OUTLINE_SEL

class TileOutlineView:
    def __init__(self, controller, editor_state):
        self.controller = controller        
        self.editor = editor_state

    def render(self, screen, camera, map):
        
        # Hover
        hover = self.controller._tile_under_mouse(pygame.mouse.get_pos(), camera, map)
        if hover:
            rect = pygame.Rect(
                camera.apply((hover.x, hover.y)),
                camera.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_HOVER, rect, 3)

        # Seleccionado
        sel = self.editor.selected_tile
        if sel:
            rect = pygame.Rect(
                camera.apply((sel.x, sel.y)),
                camera.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_SEL, rect, 3)