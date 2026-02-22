import pygame

class TabsView:
    """
    View for drawing default and ground tabs.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin

    def draw(self, surface: pygame.Surface, current_tab: str,
             default_rect: pygame.Rect, ground_rect: pygame.Rect) -> dict:
        for rect, label in ((default_rect, 'default'), (ground_rect, 'ground')):
            bg_color = (80,80,80) if current_tab == label else (60,60,60)
            pygame.draw.rect(surface, bg_color, rect)
            text_surf = self.font.render(label.capitalize(), True, (255,255,255))
            surface.blit(
                text_surf,
                (
                    rect.x + (rect.width - text_surf.get_width()) // 2,
                    rect.y + (rect.height - text_surf.get_height()) // 2
                )
            )
        return {'tab_rects': [default_rect, ground_rect]}
