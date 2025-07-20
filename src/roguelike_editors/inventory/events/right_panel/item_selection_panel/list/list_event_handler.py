import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController

class ListEventHandler:
    """
    Handle scrolling and item selection in list.
    """
    def __init__(self, controller: ItemSelectionPanelController, view):
        self.controller = controller
        self.view = view
        self.model = controller.model

    def handle(self, event):
        # Mouse-wheel or scroll click
        if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
            if self.view.scroll_panel.handle_event(event):
                return True
        # Item selection click
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            line_h = self.view.font.get_linesize()
            tab_h = line_h + self.view.margin
            items = self.view.scroll_panel.items
            visible = min(len(items), self.model.visible_count)
            scroll_h = visible * line_h + 2 * self.view.margin
            scroll_x = self.view.panel_rect.x
            scroll_y = self.view.panel_rect.y + tab_h
            scroll_rect = pygame.Rect(scroll_x, scroll_y, self.view.panel_rect.width, scroll_h)
            if scroll_rect.collidepoint(mx, my):
                offset = my - (scroll_rect.y + self.view.margin) + self.view.scroll_panel.scroll_offset
                idx = int(offset // line_h)
                if 0 <= idx < len(items):
                    item = items[idx]
                    # delegate to model via controller's list_controller
                    self.controller.list_controller.select_item(item, idx)
                    return True
        return False
