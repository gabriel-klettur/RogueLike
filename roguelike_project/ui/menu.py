import pygame

class Menu:
    def __init__(self, state, font_size=36):
        self.state = state
        self.selected = 0
        self.font = pygame.font.SysFont("Arial", font_size)
        self.surface = pygame.Surface((400, 250))
        self.surface.set_alpha(240)
        self.bg_color = (30, 30, 30)
        self.default_color = (255, 255, 255)
        self.selected_color = (255, 200, 0)

    def get_options(self):
        # Cambia el texto según el modo actual
        mode_option = "Modo local" if self.state.mode == "online" else "Modo multijugador"
        return ["Cambiar personaje", mode_option, "Salir"]

    def handle_input(self, event):
        options = self.get_options()
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_UP:
                self.selected = (self.selected - 1) % len(options)
            elif event.key == pygame.K_DOWN:
                self.selected = (self.selected + 1) % len(options)
            elif event.key == pygame.K_RETURN:
                return options[self.selected]
        return None

    def draw(self, screen):
        options = self.get_options()
        self.surface.fill(self.bg_color)
        for i, option in enumerate(options):
            color = self.selected_color if i == self.selected else self.default_color
            text = self.font.render(option, True, color)
            self.surface.blit(text, (50, 40 + i * 50))
        screen.blit(self.surface, (400, 300))
