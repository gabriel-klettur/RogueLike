# src.roguelike_project/systems/editor/tiles/view/tools/tile_toolbar_view.py

import pygame
from src.roguelike_project.systems.editor.tiles.tiles_editor_config import TOOLS

class TileToolbarView:
    def __init__(self, toolbar):
        self.toolbar = toolbar

    def render(self, screen):
        for idx, tool in enumerate(TOOLS):
            px = self.toolbar.x
            py = self.toolbar.y + idx * (self.toolbar.size + self.toolbar.padding)
            rect = pygame.Rect(px, py, self.toolbar.size, self.toolbar.size)
            self.toolbar.icon_rects[tool] = rect
            screen.blit(self.toolbar.icons[tool], (px, py))
            color = (255, 200, 0) if self.toolbar.editor.current_tool == tool else (255, 255, 255)
            pygame.draw.rect(screen, color, rect, 4)
