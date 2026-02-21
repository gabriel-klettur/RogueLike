import pygame

class TittleView:
    """
    View for panel background, border, header background, and title text.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin

    def draw(self, surface: pygame.Surface, panel_rect: pygame.Rect) -> dict:
        # Panel background & border
        pygame.draw.rect(surface, (50,50,50), panel_rect)
        pygame.draw.rect(surface, (255,255,0), panel_rect, 2)
        # Header background and title
        title = "Item List"
        title_surf = self.font.render(title, True, (255,255,255))
        header_h = title_surf.get_height() + self.margin
        header_rect = pygame.Rect(panel_rect.x, panel_rect.y - header_h, panel_rect.width, header_h)
        pygame.draw.rect(surface, (80,80,80), header_rect)
        # Title text
        surface.blit(
            title_surf,
            (
                panel_rect.x + (panel_rect.width - title_surf.get_width()) // 2,
                panel_rect.y - title_surf.get_height() - self.margin
            )
        )
        return {"panel_rect": panel_rect, "header_rect": header_rect}
