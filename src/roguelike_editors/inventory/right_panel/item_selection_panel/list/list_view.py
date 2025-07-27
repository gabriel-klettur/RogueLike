import pygame
from roguelike_ui.widgets.scroll_panel import ScrollPanel

class ListView:
    """
    View for scrollable item list, hover and selection highlights.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        self.scroll_panel = ScrollPanel(font, margin=margin)

    def draw(self, surface: pygame.Surface, items: list[str], scroll_rect: pygame.Rect,
             line_h: int, current_tab: str, selected_item: str | None,
             selected_index: int | None) -> dict:
        # Draw scroll panel
        self.scroll_panel.set_items(items)
        self.scroll_panel.draw(surface, scroll_rect)
        mx, my = pygame.mouse.get_pos()
        # Hover highlight
        if scroll_rect.collidepoint(mx, my):
            idx = (my - scroll_rect.y + self.scroll_panel.scroll_offset) // line_h
            items_list = self.scroll_panel.items
            if 0 <= idx < len(items_list):
                y0 = scroll_rect.y - self.scroll_panel.scroll_offset
                hover_rect = pygame.Rect(scroll_rect.x, y0 + idx*line_h,
                                         scroll_rect.width, line_h)
                pygame.draw.rect(surface, (255,255,0), hover_rect, 2)
        # Selection highlight
        idx_sel = None
        if current_tab == 'ground':
            if selected_index is not None and 0 <= selected_index < len(self.scroll_panel.items):
                idx_sel = selected_index
        else:
            if selected_item in self.scroll_panel.items:
                idx_sel = self.scroll_panel.items.index(selected_item)
        if idx_sel is not None:
            y0 = scroll_rect.y - self.scroll_panel.scroll_offset
            sel_rect = pygame.Rect(scroll_rect.x, y0 + idx_sel*line_h,
                                    scroll_rect.width, line_h)
            pygame.draw.rect(surface, (255,255,0), sel_rect, 2)
        return {}
