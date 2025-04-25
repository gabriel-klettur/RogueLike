# src.roguelike_project/systems/editor/tiles/view/tools/tile_outline_view.py

import pygame
from src.roguelike_engine.config import TILE_SIZE
from src.roguelike_game.systems.editor.tiles.tiles_editor_config import OUTLINE_HOVER, OUTLINE_SEL

class TileOutlineView:
    def __init__(self, controller, state, editor_state):
        self.controller = controller
        self.state = state
        self.editor = editor_state

    def render(self, screen):
        cam = self.state.camera

        # Hover
        hover = self.controller._tile_under_mouse(pygame.mouse.get_pos())
        if hover:
            rect = pygame.Rect(
                cam.apply((hover.x, hover.y)),
                cam.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_HOVER, rect, 3)

        # Seleccionado
        sel = self.editor.selected_tile
        if sel:
            rect = pygame.Rect(
                cam.apply((sel.x, sel.y)),
                cam.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_SEL, rect, 3)
