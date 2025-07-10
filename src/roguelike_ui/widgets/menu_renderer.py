import pygame

class MenuRenderer:
    """
    Se encarga de dibujar el menú en pantalla.
    """
    def __init__(self, font_size=36):
        self.font = pygame.font.SysFont("Arial", font_size)
        self.surface = pygame.Surface((400, 250))
        self.surface.set_alpha(240)
        self.bg_color = (30, 30, 30)
        self.default_color = (255, 255, 255)
        self.selected_color = (255, 200, 0)

    def draw(self, screen, selected, options):
        """
        Dibuja las opciones y retorna el rect para dirty rects.
        """
        self.surface.fill(self.bg_color)
        for i, option in enumerate(options):
            color = self.selected_color if i == selected else self.default_color
            text = self.font.render(option, True, color)
            self.surface.blit(text, (50, 40 + i * 50))
        rect = screen.blit(self.surface, (400, 300))
        return rect
