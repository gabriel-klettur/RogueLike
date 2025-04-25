# src.roguelike_project/systems/editor/tiles/tile_editor_view.py

import pygame
from src.roguelike_engine.config import TILE_SIZE
from src.roguelike_game.systems.editor.tiles.tiles_editor_config import OUTLINE_CHOICE, OUTLINE_HOVER, OUTLINE_SEL
from src.roguelike_engine.utils.loader import load_image

from src.roguelike_game.systems.editor.tiles.view.tools.tile_toolbar_view import TileToolbarView
from src.roguelike_game.systems.editor.tiles.view.tools.tile_picker_view import TilePickerView
from src.roguelike_game.systems.editor.tiles.view.tools.tile_outline_view import TileOutlineView

class TileEditorControllerView:
    def __init__(self, controller, state, editor_state):
        self.controller = controller
        self.state      = state
        self.editor     = editor_state

        self.toolbar_view = TileToolbarView(controller.toolbar)
        self.picker_view  = TilePickerView(controller.picker)
        self.outline_view = TileOutlineView(controller, state, editor_state)

    def render(self, screen):
        if not self.editor.active:
            return

        self.toolbar_view.render(screen)
        self.picker_view.render(screen)

        if self.editor.view_active:
            self._render_view_panel(screen)

        self.outline_view.render(screen)

    def _render_view_panel(self, screen):
        panel_w = TILE_SIZE + 40
        panel_h = 3 * (TILE_SIZE + 30)
        x0 = self.controller.toolbar.x + self.controller.toolbar.size + 20
        y0 = self.controller.toolbar.y

        panel = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        panel.fill((20, 20, 20, 200))
        font = pygame.font.SysFont("Arial", 14)
        mouse_pos = pygame.mouse.get_pos()

        items = [
            ("Hovered",  self.controller._tile_under_mouse(mouse_pos), OUTLINE_HOVER),
            ("Selected", self.editor.selected_tile,                     OUTLINE_SEL),
            ("Choice",   None,                                         OUTLINE_CHOICE),
        ]

        for idx, (label, tile, color) in enumerate(items):
            ty = idx * (TILE_SIZE + 30) + 10
            sprite = None
            if label == "Choice" and self.editor.current_choice:
                sprite = load_image(self.editor.current_choice, (TILE_SIZE, TILE_SIZE))
            elif tile:
                sprite = tile.sprite

            if sprite:
                panel.blit(sprite, ((panel_w - TILE_SIZE)//2, ty))
            rect = pygame.Rect((panel_w - TILE_SIZE)//2, ty, TILE_SIZE, TILE_SIZE)
            pygame.draw.rect(panel, color, rect, 3)
            text = font.render(label, True, (255, 255, 255))
            panel.blit(text, (5, ty + TILE_SIZE + 2))

        screen.blit(panel, (x0, y0))
