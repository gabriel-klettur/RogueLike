# roguelike_game/systems/editor/tiles/view/tools/tile_toolbar_view.py

# Path: src/roguelike_game/systems/editor/tiles/view/tools/tile_toolbar_view.py
import pygame
from roguelike_game.systems.editor.tiles.tiles_editor_config import TOOLS, BTN_W, BTN_H
from roguelike_engine.map.model.layer import Layer

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
        # Render layers dropdown if open
        if self.toolbar.editor.layers_view_open:
            idx = TOOLS.index("view_layers")
            icon_x = self.toolbar.x
            icon_y = self.toolbar.y + idx * (self.toolbar.size + self.toolbar.padding)
            drop_x = icon_x + self.toolbar.size + self.toolbar.padding
            drop_y = icon_y + self.toolbar.size + self.toolbar.padding
            font = pygame.font.SysFont("Arial", 14)
            self.toolbar.layer_option_rects.clear()
            for i, layer in enumerate(Layer):
                ry = drop_y + i * BTN_H
                rect = pygame.Rect(drop_x, ry, BTN_W, BTN_H)
                # background
                pygame.draw.rect(screen, (20, 20, 20), rect)
                # border indicates visibility
                border = (0, 255, 0) if self.toolbar.editor.visible_layers[layer] else (255, 0, 0)
                pygame.draw.rect(screen, border, rect, 2)
                text = font.render(layer.name, True, (255, 255, 255))
                ty = ry + (BTN_H - text.get_height()) // 2
                screen.blit(text, (drop_x + 5, ty))
                self.toolbar.layer_option_rects[layer] = rect
            # Buildings toggle option
            ry = drop_y + len(Layer) * BTN_H
            rect = pygame.Rect(drop_x, ry, BTN_W, BTN_H)
            pygame.draw.rect(screen, (20, 20, 20), rect)
            border = (0, 255, 0) if self.toolbar.editor.show_buildings else (255, 0, 0)
            pygame.draw.rect(screen, border, rect, 2)
            text = font.render("Buildings", True, (255, 255, 255))
            ty = ry + (BTN_H - text.get_height()) // 2
            screen.blit(text, (drop_x + 5, ty))
            self.toolbar.layer_option_rects["buildings"] = rect