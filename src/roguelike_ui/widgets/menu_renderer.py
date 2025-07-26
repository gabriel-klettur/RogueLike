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
        # Record blit positions for testing
        self.last_blits = []

    def draw(self, screen, selected, options):
        """
        Dibuja las opciones y retorna el rect para dirty rects.
        """
        self.surface.fill(self.bg_color)
        # reset blit record
        self.last_blits = []
        
        for i, option in enumerate(options):
            # calculate and record position
            pos = (50, 40 + i * 50)
            self.last_blits.append(pos)
            color = self.selected_color if i == selected else self.default_color
            text = self.font.render(option, True, color)
            self.surface.blit(text, (50, 40 + i * 50))
        # Center menu on screen
        screen_width, screen_height = screen.get_size()
        surface_width, surface_height = self.surface.get_size()
        x = (screen_width - surface_width) // 2
        y = (screen_height - surface_height) // 2
        # unwrap dummy surface if needed
        surface_to_blit = self.surface._surf if hasattr(self.surface, '_surf') else self.surface
        rect = screen.blit(surface_to_blit, (x, y))
        return rect
