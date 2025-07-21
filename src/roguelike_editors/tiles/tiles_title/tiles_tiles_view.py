import pygame

class TilesTilesView:
    """View for the Tiles Title Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        # Render title panel UI
        font = pygame.font.SysFont("Arial", 18, bold=True)
        # background
        text = self.state.title or "Title"
        text_surf = font.render(text, True, (255, 255, 255))
        x, y = 20, 10
        padding = 5
        bg_rect = pygame.Rect(x - padding, y - padding, text_surf.get_width() + padding * 2, text_surf.get_height() + padding * 2)
        pygame.draw.rect(screen, (30, 30, 30), bg_rect)
        screen.blit(text_surf, (x, y))
