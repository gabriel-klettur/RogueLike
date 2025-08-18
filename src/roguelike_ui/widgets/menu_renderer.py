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

    def draw_saves(self, screen, selected, items, detail_lines):
        """
        Dibuja una lista de partidas guardadas con un panel de detalles a la derecha.
        items: lista de etiquetas (str) para cada partida.
        detail_lines: lista de strings con detalles (ya formateados) de la partida seleccionada.
        """
        width, height = 800, 400
        surf = pygame.Surface((width, height))
        surf.set_alpha(240)
        surf.fill(self.bg_color)

        # Panel de lista (izquierda)
        list_x, list_y = 30, 30
        for i, label in enumerate(items):
            color = self.selected_color if i == selected else self.default_color
            text = self.font.render(label, True, color)
            surf.blit(text, (list_x, list_y + i * 40))

        # Panel de detalles (derecha)
        details_x = width // 2 + 20
        dy = 0
        for line in detail_lines[:10]:
            t = self.font.render(line, True, self.default_color)
            surf.blit(t, (details_x, 30 + dy))
            dy += 32

        # Centro en pantalla
        screen_width, screen_height = screen.get_size()
        x = (screen_width - width) // 2
        y = (screen_height - height) // 2
        surface_to_blit = surf._surf if hasattr(surf, '_surf') else surf
        rect = screen.blit(surface_to_blit, (x, y))
        return rect
