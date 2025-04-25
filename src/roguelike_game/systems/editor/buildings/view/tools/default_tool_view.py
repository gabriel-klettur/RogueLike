
import pygame

class DefaultToolView:
    def __init__(self, state, editor_state, handle_size=50):
        self.state = state
        self.editor = editor_state
        self.handle_size = handle_size

    def render_reset_handle(self, screen, building):
        x, y = self.state.camera.apply((building.x, building.y))
        w, h = self.state.camera.scale(building.image.get_size())

        reset_rect = pygame.Rect(
            x + w - 2 * self.handle_size,
            y,
            self.handle_size,
            self.handle_size
        )
        pygame.draw.rect(screen, (255, 255, 255), reset_rect)  # blanco
        pygame.draw.rect(screen, (0, 0, 0), reset_rect, 2)      # borde negro
