import pygame
from roguelike_ui.widgets.scroll_panel import ScrollPanel
from roguelike_editors.inventory.model.inventory_panel_model import InventoryPanelModel

class InventoryPanelView:
    """
    Vista para el panel izquierdo de listado (tabs + lista con scroll + highlights).
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        self.scroll_panel = ScrollPanel(font, margin=margin)
        self.tab_rects = []
        self.panel_rect = pygame.Rect(0, 0, 0, 0)

    def draw(self, surface: pygame.Surface, model: InventoryPanelModel, base_rect: pygame.Rect, items: list):
        results = {}
        # Dibujar pestañas de categoría
        self.tab_rects = []
        tab_x, tab_y = 10, 40
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
        results['tab_rects'] = self.tab_rects

        # Dibujar fondo del panel
        pygame.draw.rect(surface, (50, 50, 50), base_rect)
        pygame.draw.rect(surface, (255, 255, 255), base_rect, 2)
        self.panel_rect = base_rect
        results['panel_rect'] = self.panel_rect

        # Dibujar lista scrollable
        self.scroll_panel.set_items(items)
        self.scroll_panel.draw(surface, self.panel_rect)
        results['list_rect'] = self.panel_rect

        # Highlight permanente para monsters
        if model.current_category == 'monsters' and model.selected_eid:
            line_h = self.font.get_linesize()
            y0 = self.panel_rect.y - self.scroll_panel.scroll_offset
            for idx, line in enumerate(items):
                if not line.startswith(' ') and line.split()[0] == str(model.selected_eid):
                    r = pygame.Rect(self.panel_rect.x, y0 + idx*line_h, self.panel_rect.width, line_h)
                    pygame.draw.rect(surface, (255, 255, 0), r, 3)
                    break

        # Highlight on hover para monsters
        mx, my = pygame.mouse.get_pos()
        if model.current_category == 'monsters' and self.panel_rect.collidepoint(mx, my):
            line_h = self.font.get_linesize()
            idx = (my - self.panel_rect.y + self.scroll_panel.scroll_offset) // line_h
            if 0 <= idx < len(items) and not items[idx].startswith(' '):
                y0 = self.panel_rect.y - self.scroll_panel.scroll_offset
                r = pygame.Rect(self.panel_rect.x, y0 + idx*line_h, self.panel_rect.width, line_h)
                pygame.draw.rect(surface, (255, 255, 0), r, 2)

        return results