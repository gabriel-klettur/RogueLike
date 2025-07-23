import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD, CLR_SELECTION, CLR_BORDER

class BrushPanelView:
    """
    View for the Brush Size Panel.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        if not self.state.visible:
            return
        font = pygame.font.SysFont("Arial", 14)
        # Position to the right of toolbar panel
        toolbar = self.controller.editor_controller.toolbar
        x0 = toolbar.x + toolbar.size + toolbar.padding
        y0 = toolbar.y
        # Clear previous rects
        self.state.option_rects.clear()
        # Render size options
        for i, (w, h) in enumerate(self.state.sizes):
            ry = y0 + i * BTN_H
            rect = pygame.Rect(x0, ry, BTN_W, BTN_H)
            pygame.draw.rect(screen, (20, 20, 20), rect)
            # Border highlight for selected
            border_color = CLR_SELECTION if i == self.state.selected_index else CLR_BORDER
            pygame.draw.rect(screen, border_color, rect, 2)
            text = font.render(f"{w}x{h}", True, (255, 255, 255))
            ty = ry + (BTN_H - text.get_height()) // 2
            screen.blit(text, (x0 + PAD, ty))
            self.state.option_rects[i] = rect
        # Draw outer border
        panel_h = len(self.state.sizes) * BTN_H
        pygame.draw.rect(screen, CLR_SELECTION, pygame.Rect(x0, y0, BTN_W, panel_h), 3)