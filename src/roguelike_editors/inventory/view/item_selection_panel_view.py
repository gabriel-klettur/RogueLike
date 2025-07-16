import pygame
from roguelike_ui.widgets.scroll_panel import ScrollPanel
from roguelike_editors.inventory.model.item_selection_panel_model import ItemSelectionPanelModel

class ItemSelectionPanelView:
    def __init__(self, font: pygame.font.Font, margin: int = 5, button_size: tuple[int,int] = (120,30)):
        self.font = font
        self.margin = margin
        self.button_size = button_size
        self.scroll_panel = ScrollPanel(self.font, margin=self.margin)
        self.panel_rect = pygame.Rect(0,0,0,0)
        self.header_rect = pygame.Rect(0,0,0,0)
        self.add_button_rect = pygame.Rect(0,0,0,0)

    def draw(self, surface: pygame.Surface, model: ItemSelectionPanelModel, base_rect: pygame.Rect):
        if not model.show_panel:
            return {}
        line_h = self.font.get_linesize()
        visible = min(len(model.available_items), model.visible_count)
        w = 200
        h = visible * line_h + 2*self.margin
        x = base_rect.centerx - w//2 + int(model.drag_offset.x)
        y = base_rect.centery - h//2 + int(model.drag_offset.y)
        self.panel_rect = pygame.Rect(x,y,w,h)
        # Panel background & border
        pygame.draw.rect(surface, (50,50,50), self.panel_rect)
        pygame.draw.rect(surface, (255,255,255), self.panel_rect, 2)
        # Header background
        title = "Item List"
        title_surf = self.font.render(title, True, (255,255,255))
        header_h = title_surf.get_height() + self.margin
        self.header_rect = pygame.Rect(x, y-header_h, w, header_h)
        pygame.draw.rect(surface, (80,80,80), self.header_rect)
        # Title text
        surface.blit(title_surf, (x + (w-title_surf.get_width())//2, y-title_surf.get_height()-self.margin))
        # Scrollable list
        self.scroll_panel.set_items(model.available_items)
        self.scroll_panel.draw(surface, self.panel_rect)
        # Highlight selected
        if model.selected_item in self.scroll_panel.items:
            idx = self.scroll_panel.items.index(model.selected_item)
            y0 = y - self.scroll_panel.scroll_offset
            sel_rect = pygame.Rect(x, y0 + idx*line_h, w, line_h)
            pygame.draw.rect(surface, (255,255,0), sel_rect, 2)
        # Add button
        bx = x + self.margin
        by = y + h + self.margin
        bw, bh = w - 2*self.margin, self.button_size[1]
        self.add_button_rect = pygame.Rect(bx, by, bw, bh)
        pygame.draw.rect(surface, (100,100,100), self.add_button_rect)
        border = (255,255,0) if self.add_button_rect.collidepoint(pygame.mouse.get_pos()) else (255,255,255)
        pygame.draw.rect(surface, border, self.add_button_rect, 2)
        txt = self.font.render("Add to Inventory", True, (255,255,255))
        surface.blit(txt, (bx + (bw - txt.get_width())//2, by + (bh - line_h)//2))
        return {
            "panel_rect": self.panel_rect,
            "header_rect": self.header_rect,
            "add_button_rect": self.add_button_rect
        }
