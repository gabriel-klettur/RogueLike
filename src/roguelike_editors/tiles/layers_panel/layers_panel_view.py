import pygame
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD, CLR_SELECTION

class LayersPanelView:
    """View for the Layers Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        # Render layers panel as popup menu

        font = pygame.font.SysFont("Arial", 14)
        # Panel position (aligned with Layers button icon)
        if self.state.pos is not None:
            x0, y0 = self.state.pos
        else:
            toolbar = self.controller.editor_controller.toolbar
            icon_rect = toolbar.icon_rects.get("view_layers")
            if icon_rect:
                x0 = icon_rect.right + toolbar.padding
                y0 = icon_rect.y
            else:
                # fallback next to toolbar
                x0 = toolbar.x + toolbar.size + PAD
                y0 = toolbar.y
        # Clear previous rects
        self.state.option_rects.clear()
        # Render layer toggles
        for i, layer in enumerate(Layer):
            ry = y0 + i * BTN_H
            rect = pygame.Rect(x0, ry, BTN_W, BTN_H)
            pygame.draw.rect(screen, (20, 20, 20), rect)
            visible = self.state.visible_layers.get(layer, False)
            border = (0, 255, 0) if visible else (255, 0, 0)
            pygame.draw.rect(screen, border, rect, 2)
            text = font.render(layer.name, True, (255, 255, 255))
            ty = ry + (BTN_H - text.get_height()) // 2
            screen.blit(text, (x0 + 5, ty))
            self.state.option_rects[layer] = rect
        # Render buildings toggle
        ry = y0 + len(list(Layer)) * BTN_H
        rect = pygame.Rect(x0, ry, BTN_W, BTN_H)
        pygame.draw.rect(screen, (20, 20, 20), rect)
        sb = self.controller.editor_state.toolbar_state.show_buildings
        border = (128, 0, 128) if sb else (255, 0, 0)
        pygame.draw.rect(screen, border, rect, 2)
        text = font.render("Buildings", True, (255, 255, 255))
        ty = ry + (BTN_H - text.get_height()) // 2
        screen.blit(text, (x0 + 5, ty))
        self.state.option_rects["buildings"] = rect
        panel_h = (len(list(Layer)) + 1) * BTN_H
        pygame.draw.rect(screen, CLR_SELECTION, pygame.Rect(x0, y0, BTN_W, panel_h), 3)
