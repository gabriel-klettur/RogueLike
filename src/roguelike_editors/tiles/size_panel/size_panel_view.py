import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD, CLR_SELECTION, CLR_BORDER

class SizePanelView:
    """
    View for the Size Panel.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        if not self.state.visible:
            return
        font = pygame.font.SysFont("Arial", 14)
        # Panel position (draggable override or right of toolbar panel)
        if self.state.pos is not None:
            x0, y0 = self.state.pos
        else:
            toolbar = self.controller.editor_controller.toolbar
            x0 = toolbar.x + toolbar.size + toolbar.padding
            y0 = toolbar.y
        # Clear previous rects
        self.state.option_rects.clear()
        # Render size options
        for i, (w, h) in enumerate(self.state.sizes):
            ry = y0 + i * BTN_H
            rect = pygame.Rect(x0, ry, BTN_W, BTN_H)
            if i == self.state.selected_index:
                bg_color = CLR_SELECTION
                text_color = (0, 0, 0)
            else:
                bg_color = (20, 20, 20)
                text_color = (255, 255, 255)
            pygame.draw.rect(screen, bg_color, rect)
            pygame.draw.rect(screen, CLR_BORDER, rect, 2)
            text = font.render(f"{w}x{h}", True, text_color)
            ty = ry + (BTN_H - text.get_height()) // 2
            screen.blit(text, (x0 + PAD, ty))
            self.state.option_rects[i] = rect
        # Draw outer border
        panel_h = len(self.state.sizes) * BTN_H
        pygame.draw.rect(screen, CLR_SELECTION, pygame.Rect(x0, y0, BTN_W, panel_h), 3)