import pygame
from roguelike_ui.widgets.text_input import TextInput

class InputView:
    """
    View for quantity input field.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        self.text_input = TextInput(font)

    def draw(self, surface: pygame.Surface, quantity: int, input_rect: pygame.Rect) -> dict:
        line_h = self.font.get_linesize()
        # Input field background
        pygame.draw.rect(surface, (30,30,30), input_rect)
        # Border color: yellow on hover or if active
        mx, my = pygame.mouse.get_pos()
        border_color = (255,255,0) if input_rect.collidepoint(mx, my) or self.text_input.active else (255,255,255)
        pygame.draw.rect(surface, border_color, input_rect, 1)
        # Sync TextInput text when inactive
        if not self.text_input.active:
            self.text_input.text = str(quantity)
        # Draw TextInput (text + blinking caret)
        self.text_input.draw(surface, input_rect.x + 5, input_rect.y + (input_rect.height - line_h)//2)
        return {"input_rect": input_rect}
