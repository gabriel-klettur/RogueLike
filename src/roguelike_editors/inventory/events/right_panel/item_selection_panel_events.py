import pygame
from roguelike_editors.inventory.controller.item_selection_panel_controller import ItemSelectionPanelController

class ItemSelectionPanelEventHandler:
    def __init__(self, controller: ItemSelectionPanelController, view):
        self.controller = controller
        self.view = view
        self.model = controller.model

    def handle(self, event):
        # Only handle when panel is shown
        if not self.model.show_panel:
            return False
        # Activate quantity input on click
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if hasattr(self.view, 'input_rect') and self.view.input_rect.collidepoint(event.pos):
                self.view.text_input.activate(initial_text=str(self.model.quantity), select_all=True)
                return True
        # Handle text input events
        if self.view.text_input.handle_event(event):
            try:
                self.model.quantity = int(self.view.text_input.text)
            except ValueError:
                self.model.quantity = 1
            return True
        # Close panel if click outside
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            if not self.view.panel_rect.collidepoint(mx, my) and not self.view.header_rect.collidepoint(mx, my):
                self.controller.close()
                return True
        # Drag header
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if self.view.header_rect.collidepoint(event.pos):
                self.model.dragging = True
                self.model.drag_start_pos = pygame.Vector2(event.pos) - self.model.drag_offset
                return True
        if event.type == pygame.MOUSEMOTION and self.model.dragging:
            self.model.drag_offset = pygame.Vector2(event.pos) - self.model.drag_start_pos
            return True
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and self.model.dragging:
            self.model.dragging = False
            return True
        # Mouse-wheel scroll
        if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
            if self.view.scroll_panel.handle_event(event):
                return True
        # Click events
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            # Select item
            panel = self.view.panel_rect
            if panel.collidepoint(mx, my):
                line_h = self.view.font.get_linesize()
                idx = (my - panel.y + self.model.scroll_offset) // line_h
                items = self.model.available_items
                if 0 <= idx < len(items):
                    self.controller.select_item(items[idx])
                    return True
            # Confirm add
            btn = self.view.add_button_rect
            if btn.collidepoint(mx, my):
                self.controller.confirm()
                return True
        return False
