import pygame
from .tabs import TabsView
from .list import ListView


class PanelView:
    """
    Vista del panel izquierdo (tabs + lista con scroll + highlights), delegando a TabsView y ListView.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        self.tabs_view = TabsView(font, margin)
        self.list_view = ListView(font, margin)
        # Para compatibilidad con handlers
        self.tab_rects = []
        self.panel_rect = pygame.Rect(0, 0, 0, 0)

    def draw(self, surface: pygame.Surface, model, base_rect: pygame.Rect, items: list):
        """
        Dibuja pestañas y lista. Retorna un dict con 'tab_rects', 'panel_rect' y 'list_rect'.
        """
        results = {}
        # Dibujar tabs
        self.tab_rects = self.tabs_view.draw(surface, model)
        results['tab_rects'] = self.tab_rects
        # Dibujar lista y highlights
        list_results = self.list_view.draw(surface, model, base_rect, items)
        results.update(list_results)
        # Actualizar panel_rect para compatibilidad
        self.panel_rect = list_results.get('panel_rect')
        return results
