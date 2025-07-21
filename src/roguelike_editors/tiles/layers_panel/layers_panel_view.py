import pygame
from roguelike_engine.map.model.layer import Layer

class LayersPanelView:
    """View for the Layers Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        # Render layers panel UI

        font = pygame.font.SysFont("Arial", 14)
        # Panel position
        x0, y0 = 20, 60
        # Clear previous rects
        self.state.option_rects.clear()
        for idx, layer in enumerate(self.state.visible_layers):
            y = y0 + idx * (20 + 10)
            x = x0
            # Checkbox
            rect = pygame.Rect(x, y, 20, 20)
            self.state.option_rects[layer] = rect
            if self.state.visible_layers[layer]:
                pygame.draw.rect(screen, (0, 255, 0), rect)
            else:
                pygame.draw.rect(screen, (255, 0, 0), rect)
            pygame.draw.rect(screen, (255, 255, 255), rect, 2)
            # Label
            text_surf = font.render(layer.name, True, (255, 255, 255))
            screen.blit(text_surf, (x + 30, y + (20 - text_surf.get_height()) // 2))
