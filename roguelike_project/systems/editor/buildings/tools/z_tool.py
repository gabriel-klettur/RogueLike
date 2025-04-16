import pygame

class ZTool:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor_state = editor_state

        self.button_size = (30, 30)
        self.font = pygame.font.SysFont("Arial", 16)

    def render(self, screen, building):
        x, y = self.state.camera.apply((building.x, building.y))
        w, h = self.state.camera.scale(building.image.get_size())

        # Panel centrado abajo del edificio
        panel_width = 120
        panel_height = 40
        panel_x = x + (w - panel_width) // 2
        panel_y = y + h - 80  # ligeramente separado del edificio

        # Fondo del panel (negro semitransparente)
        panel_surface = pygame.Surface((panel_width, panel_height), pygame.SRCALPHA)
        panel_surface.fill((0, 0, 0, 190))  # RGBA

        # Borde blanco
        pygame.draw.rect(panel_surface, (255, 255, 255), panel_surface.get_rect(), 2, border_radius=6)

        # Botón -
        self.minus_rect = pygame.Rect(5, 5, *self.button_size)
        pygame.draw.rect(panel_surface, (50, 50, 50), self.minus_rect, border_radius=4)
        minus_text = self.font.render("-", True, (255, 255, 255))
        panel_surface.blit(minus_text, (self.minus_rect.x + 10, self.minus_rect.y + 5))

        # Texto central Z
        z_value = self.font.render(f"Z: {building.z}", True, (255, 255, 255))
        text_rect = z_value.get_rect(center=(panel_width // 2, panel_height // 2))
        panel_surface.blit(z_value, text_rect)

        # Botón +
        self.plus_rect = pygame.Rect(panel_width - 5 - self.button_size[0], 5, *self.button_size)
        pygame.draw.rect(panel_surface, (50, 50, 50), self.plus_rect, border_radius=4)
        plus_text = self.font.render("+", True, (255, 255, 255))
        panel_surface.blit(plus_text, (self.plus_rect.x + 10, self.plus_rect.y + 5))

        # Blit final del panel
        screen.blit(panel_surface, (panel_x, panel_y))

    def handle_mouse_click(self, pos):
        if not self.editor_state.selected_building:
            return

        if hasattr(self, 'minus_rect') and self.minus_rect.collidepoint(pos):
            self.editor_state.selected_building.z -= 1
            print(f"⬅️ Z disminuido a: {self.editor_state.selected_building.z}")

        elif hasattr(self, 'plus_rect') and self.plus_rect.collidepoint(pos):
            self.editor_state.selected_building.z += 1
            print(f"➡️ Z aumentado a: {self.editor_state.selected_building.z}")
