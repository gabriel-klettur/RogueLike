import pygame
from roguelike_ui.widgets.scroll_panel import ScrollPanel
from roguelike_ui.widgets.text_input import TextInput
from roguelike_editors.inventory.model.right_panel.item_selection_panel_model import ItemSelectionPanelModel

class ItemSelectionPanelView:
    def __init__(self, font: pygame.font.Font, margin: int = 5, button_size: tuple[int,int] = (120,30)):
        self.font = font
        self.margin = margin
        self.button_size = button_size
        self.scroll_panel = ScrollPanel(self.font, margin=self.margin)
        self.text_input = TextInput(self.font)
        self.panel_rect = pygame.Rect(0,0,0,0)
        self.header_rect = pygame.Rect(0,0,0,0)
        self.add_button_rect = pygame.Rect(0,0,0,0)

    def draw(self, surface: pygame.Surface, model: ItemSelectionPanelModel, base_rect: pygame.Rect):
        if not model.show_panel:
            return {}
        line_h = self.font.get_linesize()
        # Determine items based on current tab
        items_list = model.default_items if model.current_tab == 'default' else model.ground_items
        visible = min(len(items_list), model.visible_count)
        # Panel width matches grid width
        w = base_rect.width
        scroll_h = visible * line_h + 2*self.margin
        input_h = line_h + 2*self.margin
        button_h = self.button_size[1]
        # Calculate panel height including tabs
        tab_h = line_h + self.margin
        panel_h = tab_h + scroll_h + self.margin + input_h + self.margin + button_h + self.margin
        # Position panel below the inventory grid
        # Align panel left edge with grid
        # Position panel in bottom-right corner of overlay
        sw, sh = surface.get_size()
        x = sw - w - self.margin + int(model.drag_offset.x)
        y = sh - panel_h - self.margin + int(model.drag_offset.y)
        self.panel_rect = pygame.Rect(x, y, w, panel_h)
        # Panel background & border
        pygame.draw.rect(surface, (50,50,50), self.panel_rect)
        pygame.draw.rect(surface, (255,255,0), self.panel_rect, 2)
        # Header background
        title = "Item List"
        title_surf = self.font.render(title, True, (255,255,255))
        header_h = title_surf.get_height() + self.margin
        self.header_rect = pygame.Rect(x, y-header_h, w, header_h)
        pygame.draw.rect(surface, (80,80,80), self.header_rect)
        # Title text
        surface.blit(title_surf, (x + (w-title_surf.get_width())//2, y-title_surf.get_height()-self.margin))
        # Tabs
        tab_w = w // 2
        default_tab_rect = pygame.Rect(x, y, tab_w, tab_h)
        ground_tab_rect = pygame.Rect(x + tab_w, y, w - tab_w, tab_h)
        self.tab_rects = [default_tab_rect, ground_tab_rect]
        # Draw tabs
        for rect, label in ((default_tab_rect, 'default'), (ground_tab_rect, 'ground')):
            bg_color = (80,80,80) if model.current_tab == label else (60,60,60)
            pygame.draw.rect(surface, bg_color, rect)
            text_surf = self.font.render(label.capitalize(), True, (255,255,255))
            surface.blit(text_surf, (rect.x + (rect.width-text_surf.get_width())//2, rect.y + (rect.height-text_surf.get_height())//2))
        # Scrollable list
        scroll_rect = pygame.Rect(x, y + tab_h, w, scroll_h)
        self.scroll_panel.set_items(items_list)
        self.scroll_panel.draw(surface, scroll_rect)
        # Hover highlight
        mx, my = pygame.mouse.get_pos()
        if scroll_rect.collidepoint(mx, my):
            idx = (my - scroll_rect.y + self.scroll_panel.scroll_offset) // line_h
            items = self.scroll_panel.items
            if 0 <= idx < len(items):
                y0 = scroll_rect.y - self.scroll_panel.scroll_offset
                hover_rect = pygame.Rect(x, y0 + idx*line_h, w, line_h)
                pygame.draw.rect(surface, (255,255,0), hover_rect, 2)
        # Highlight selected
        idx = None
        if getattr(model, 'current_tab', None) == 'ground' and hasattr(model, 'selected_index'):
            if model.selected_index is not None and 0 <= model.selected_index < len(self.scroll_panel.items):
                idx = model.selected_index
        else:
            if model.selected_item in self.scroll_panel.items:
                idx = self.scroll_panel.items.index(model.selected_item)
        if idx is not None:
            y0 = scroll_rect.y - self.scroll_panel.scroll_offset
            sel_rect = pygame.Rect(x, y0 + idx*line_h, w, line_h)
            pygame.draw.rect(surface, (255,255,0), sel_rect, 2)
        # Quantity: label + input field
        in_x = x + self.margin
        in_y = y + tab_h + scroll_h + self.margin
        in_h = input_h
        # Render label
        label_surf = self.font.render("Quantity:", True, (255,255,255))
        label_w, label_h = label_surf.get_size()
        label_y = in_y + (in_h - label_h) // 2
        surface.blit(label_surf, (in_x, label_y))
        # Input field background
        input_x = in_x + label_w + self.margin
        in_w = w - 2*self.margin - label_w - self.margin
        input_rect = pygame.Rect(input_x, in_y, in_w, in_h)
        pygame.draw.rect(surface, (30,30,30), input_rect)
        # Border color: yellow on hover or if active
        mx, my = pygame.mouse.get_pos()
        border_color = (255,255,0) if input_rect.collidepoint(mx, my) or self.text_input.active else (255,255,255)
        pygame.draw.rect(surface, border_color, input_rect, 1)
        # Sync TextInput text when inactive
        if not self.text_input.active:
            self.text_input.text = str(model.quantity)
        # Draw TextInput (text + blinking caret)
        self.text_input.draw(surface, input_x + 5, in_y + (in_h - line_h) // 2)
        self.input_rect = input_rect

        # Add button
        btn_x = in_x
        btn_y = in_y + in_h + self.margin
        btn_w = in_w
        btn_h = button_h
        self.add_button_rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
        pygame.draw.rect(surface, (100,100,100), self.add_button_rect)
        border_color = (255,255,0) if self.add_button_rect.collidepoint(pygame.mouse.get_pos()) else (255,255,255)
        pygame.draw.rect(surface, border_color, self.add_button_rect, 2)
        txt = self.font.render("Add to Inventory", True, (255,255,255))
        surface.blit(txt, (btn_x + (btn_w - txt.get_width())//2, btn_y + (btn_h - line_h)//2))
        return {
            "panel_rect": self.panel_rect,
            "header_rect": self.header_rect,
            "input_rect": input_rect,
            "add_button_rect": self.add_button_rect
        }
