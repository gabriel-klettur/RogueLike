import pygame

class TilesTilesView:
    """View for the Tiles Title Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        # Professional semi-transparent title panel
        # Ensure default title
        title_text = self.state.title or "TILES EDITOR"
        font = pygame.font.SysFont("Arial", 24, bold=True)
        text_surf = font.render(title_text, True, (255, 255, 255))
        padding_x, padding_y = 12, 8
        x, y = 10, 10
        bg_w = text_surf.get_width() + padding_x * 2
        bg_h = text_surf.get_height() + padding_y * 2
        # Create semi-transparent background surface
        bg_surf = pygame.Surface((bg_w, bg_h), pygame.SRCALPHA)
        bg_surf.fill((0, 0, 0, 180))
        # Draw semi-transparent border
        pygame.draw.rect(bg_surf, (255, 255, 255, 200), bg_surf.get_rect(), 2, border_radius=6)
        # Blit background and text
        screen.blit(bg_surf, (x, y))
        screen.blit(text_surf, (x + padding_x, y + padding_y))
