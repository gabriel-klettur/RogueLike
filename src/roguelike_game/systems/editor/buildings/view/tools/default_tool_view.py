
# Path: src/roguelike_game/systems/editor/buildings/view/tools/default_tool_view.py
import pygame

class DefaultToolView:
    def __init__(self, state, editor_state, handle_size=50):
        self.state = state
        self.editor = editor_state
        self.handle_size = handle_size

    def render_reset_handle(self, screen, building, camera):
        x, y = camera.apply((building.x, building.y))
        w, h = camera.scale(building.image.get_size())

        # Botón rojo de eliminar
        delete_rect = pygame.Rect(
            x + w - 3 * self.handle_size,
            y,
            self.handle_size,
            self.handle_size
        )
        pygame.draw.rect(screen, (220, 40, 40), delete_rect)  # rojo
        pygame.draw.rect(screen, (0, 0, 0), delete_rect, 2)   # borde negro
        # Dibuja una X blanca
        pygame.draw.line(screen, (255,255,255), delete_rect.topleft, delete_rect.bottomright, 3)
        pygame.draw.line(screen, (255,255,255), delete_rect.topright, delete_rect.bottomleft, 3)

        # Botón blanco de reset (default)
        reset_rect = pygame.Rect(
            x + w - 2 * self.handle_size,
            y,
            self.handle_size,
            self.handle_size
        )
        pygame.draw.rect(screen, (255, 255, 255), reset_rect)  # blanco
        pygame.draw.rect(screen, (0, 0, 0), reset_rect, 2)      # borde negro

    def get_delete_handle_rect(self, building, camera):
        x, y = camera.apply((building.x, building.y))
        w, h = camera.scale(building.image.get_size())
        return pygame.Rect(
            x + w - 3 * self.handle_size,
            y,
            self.handle_size,
            self.handle_size
        )