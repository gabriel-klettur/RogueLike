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

        # Panel Z centrado abajo
        panel_width = 120
        panel_height = 30
        panel_x = x + (w - panel_width) // 2
        panel_y = y + h - panel_height - 10

        # Fondo panel
        pygame.draw.rect(screen, (0, 0, 0), (panel_x, panel_y, panel_width, panel_height))
        pygame.draw.rect(screen, (255, 255, 255), (panel_x, panel_y, panel_width, panel_height), 1)

        # Botón -
        self.minus_rect = pygame.Rect(panel_x + 5, panel_y + 5, *self.button_size)
        pygame.draw.rect(screen, (80, 80, 80), self.minus_rect)
        screen.blit(self.font.render("-", True, (255, 255, 255)), (self.minus_rect.x + 10, self.minus_rect.y + 5))

        # Texto Z
        z_text = self.font.render(f"Z: {building.z}", True, (255, 255, 255))
        screen.blit(z_text, (panel_x + 45, panel_y + 7))

        # Botón +
        self.plus_rect = pygame.Rect(panel_x + 85, panel_y + 5, *self.button_size)
        pygame.draw.rect(screen, (80, 80, 80), self.plus_rect)
        screen.blit(self.font.render("+", True, (255, 255, 255)), (self.plus_rect.x + 10, self.plus_rect.y + 5))

    def handle_mouse_click(self, pos):
        if not self.editor_state.selected_building:
            return

        if hasattr(self, 'minus_rect') and self.minus_rect.collidepoint(pos):
            self.editor_state.selected_building.z -= 1
            print(f"⬅️ Z disminuido a: {self.editor_state.selected_building.z}")

        elif hasattr(self, 'plus_rect') and self.plus_rect.collidepoint(pos):
            self.editor_state.selected_building.z += 1
            print(f"➡️ Z aumentado a: {self.editor_state.selected_building.z}")
