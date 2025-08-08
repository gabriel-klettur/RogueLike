import pygame


class TabsView:
    """
    Vista para las pestañas de categoría en el panel izquierdo.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        self.tab_rects = []
        # Posición base configurable (por defecto como antes)
        self.base_x = 10
        self.base_y = 40

    def set_base_pos(self, x: int, y: int):
        self.base_x = x
        self.base_y = y

    def draw(self, surface: pygame.Surface, model) -> list:
        """
        Dibuja las pestañas y devuelve la lista de rects con su categoría.
        """
        self.tab_rects = []
        tab_x, tab_y = self.base_x, self.base_y
        for cat in model.categories:
            label = cat.capitalize()
            surf = self.font.render(label, True, (255, 255, 255))
            w, h = surf.get_size()
            padding = 10
            rect = pygame.Rect(tab_x, tab_y, w + padding*2, h + padding//2)
            color = (100, 100, 100) if model.current_category == cat else (50, 50, 50)
            pygame.draw.rect(surface, color, rect)
            pygame.draw.rect(surface, (255, 255, 255), rect, 2)
            if model.current_category == cat:
                pygame.draw.rect(surface, (255, 255, 0), rect, 2)
            surface.blit(surf, (tab_x + padding, tab_y + (rect.height - h)//2))
            self.tab_rects.append((rect, cat))
            tab_x += rect.width + 5
        return self.tab_rects
