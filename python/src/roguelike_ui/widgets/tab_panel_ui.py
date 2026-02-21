import pygame

class TabPanelUI:
    """
    Widget para mostrar pestañas y manejar selección de categoría.
    """
    def __init__(self, font: pygame.font.Font, padding: int = 10):
        self.font = font
        self.padding = padding
        self.tab_rects: list[tuple[pygame.Rect, str]] = []

    def draw(self, surface: pygame.Surface, x: int, y: int, tabs: list[str], selected: str) -> None:
        self.tab_rects = []
        for tab in tabs:
            txt = self.font.render(tab, True, (255,255,255))
            w, h = txt.get_size()
            rect = pygame.Rect(x, y, w + self.padding*2, h + self.padding//2)
            color = (100,100,100) if tab == selected else (50,50,50)
            pygame.draw.rect(surface, color, rect)
            pygame.draw.rect(surface, (255,255,255), rect, 2)
            surface.blit(txt, (x + self.padding, y + (rect.height - h)//2))
            self.tab_rects.append((rect, tab))
            x += rect.width + 5

    def handle_event(self, event: pygame.event.Event) -> str | None:
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            for rect, tab in self.tab_rects:
                if rect.collidepoint(event.pos):
                    return tab
        return None
