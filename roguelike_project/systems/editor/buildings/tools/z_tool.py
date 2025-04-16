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

        panel_width = 120
        panel_height = 40
        panel_x = x + (w - panel_width) // 2
        panel_y = y + h - 50  # altura ajustada

        # Fondo panel semitransparente
        panel_surface = pygame.Surface((panel_width, panel_height), pygame.SRCALPHA)
        panel_surface.fill((0, 0, 0, 190))
        pygame.draw.rect(panel_surface, (255, 255, 255), panel_surface.get_rect(), 2, border_radius=6)

        # Botón “–”
        minus_rect = pygame.Rect(5, 5, *self.button_size)
        pygame.draw.rect(panel_surface, (50, 50, 50), minus_rect, border_radius=4)
        minus_text = self.font.render("-", True, (255, 255, 255))
        panel_surface.blit(minus_text, (minus_rect.x + 10, minus_rect.y + 5))

        # Texto que muestra el valor de Z
        z_value = self.font.render(f"Z: {building.z}", True, (255, 255, 255))
        text_rect = z_value.get_rect(center=(panel_width // 2, panel_height // 2))
        panel_surface.blit(z_value, text_rect)

        # Botón “+”
        plus_rect = pygame.Rect(panel_width - 5 - self.button_size[0], 5, *self.button_size)
        pygame.draw.rect(panel_surface, (50, 50, 50), plus_rect, border_radius=4)
        plus_text = self.font.render("+", True, (255, 255, 255))
        panel_surface.blit(plus_text, (plus_rect.x + 10, plus_rect.y + 5))

        # Blit final: se dibuja el panel en la posición calculada
        screen.blit(panel_surface, (panel_x, panel_y))

        # Guardar las zonas clicables del panel en el objeto building
        building._ztool_bounds = {
            "panel_pos": (panel_x, panel_y),
            "minus_rect": minus_rect,
            "plus_rect": plus_rect
        }

    def handle_mouse_click(self, mouse_pos):
        for building in self.state.buildings:
            bounds = getattr(building, "_ztool_bounds", None)
            if not bounds:
                continue

            panel_x, panel_y = bounds["panel_pos"]
            minus_rect = bounds["minus_rect"].move(panel_x, panel_y)
            plus_rect = bounds["plus_rect"].move(panel_x, panel_y)

            if minus_rect.collidepoint(mouse_pos):
                building.z = max(0, building.z - 1)
                # Sincronizar en el z_state para que se guarde el valor actualizado
                self.state.z_state.set(building, building.z)
                print(f"⬅️ Z de {building.image_path} disminuido a: {building.z}")
                return

            if plus_rect.collidepoint(mouse_pos):
                building.z += 1
                # Sincronizar en el z_state para que se guarde el valor actualizado
                self.state.z_state.set(building, building.z)
                print(f"➡️ Z de {building.image_path} aumentado a: {building.z}")
                return
