import pygame

class ButtonView:
    """
    View for confirm button (Add to Inventory).
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5, button_size: tuple[int,int] = (120,30)):
        self.font = font
        self.margin = margin
        self.button_size = button_size

    def draw(self, surface: pygame.Surface, button_rect: pygame.Rect) -> dict:
        pygame.draw.rect(surface, (100,100,100), button_rect)
        mx, my = pygame.mouse.get_pos()
        border_color = (255,255,0) if button_rect.collidepoint(mx, my) else (255,255,255)
        pygame.draw.rect(surface, border_color, button_rect, 2)
        line_h = self.font.get_linesize()
        txt = self.font.render("Add to Inventory", True, (255,255,255))
        surface.blit(
            txt,
            (
                button_rect.x + (button_rect.width - txt.get_width())//2,
                button_rect.y + (button_rect.height - line_h)//2
            )
        )
        return {"add_button_rect": button_rect}
