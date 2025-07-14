import pygame
from roguelike_ui.widgets.scroll_panel import ScrollPanel

class ListPanelUI:
    """
    Widget para mostrar una lista de cadenas con scroll y selección por click.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.panel = ScrollPanel(font, margin)
        self.items = []  # list of display strings
        self.rect = pygame.Rect(0, 0, 0, 0)

    def set_items(self, items: list[str]):
        self.items = items
        self.panel.set_items(items)

    def draw(self, surface: pygame.Surface, rect: pygame.Rect):
        self.rect = rect
        self.panel.draw(surface, rect)

    def handle_event(self, event: pygame.event.Event) -> bool:
        return self.panel.handle_event(event)

    def get_selected(self, mouse_pos: tuple) -> int | None:
        """
        Devuelve el índice de ítem seleccionado por click en mouse_pos, o None.
        """
        if not self.rect.collidepoint(mouse_pos):
            return None
        line_h = self.font.get_linesize()
        # Ajustar por scroll offset
        y = mouse_pos[1] - self.rect.y + self.panel.scroll_offset
        idx = y // line_h
        if 0 <= idx < len(self.items):
            return idx
        return None
