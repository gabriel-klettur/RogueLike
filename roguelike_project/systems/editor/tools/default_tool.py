import pygame

class DefaultTool:
    def __init__(self, state, editor_state, handle_size=50):
        self.state = state
        self.editor = editor_state
        self.handle_size = handle_size

    def check_reset_handle_click(self, mx, my, building):
        """
        Verifica si el clic fue sobre el handle de reset (blanco),
        que se ubica a la izquierda del handle azul de resize.
        """
        bx, by = self.state.camera.apply((building.x, building.y))
        bw, bh = self.state.camera.scale(building.image.get_size())

        reset_rect = pygame.Rect(
            bx + bw - 2 * self.handle_size,
            by,
            self.handle_size,
            self.handle_size
        )
        return reset_rect.collidepoint(mx, my)

    def apply_reset(self, building):
        building.reset_to_original_size()

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
