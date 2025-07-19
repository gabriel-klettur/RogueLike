import pygame
from roguelike_editors.inventory.controller.right_panel.item_selection_panel_controller import ItemSelectionPanelController

class ItemSelectionPanelEventHandler:
    """
    Event handler para el panel de selección de ítems que coordina
    con el controlador de grid para agregar ítems.
    """
    def __init__(self, grid_controller, controller: ItemSelectionPanelController, view):
        self.grid_controller = grid_controller
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
        # Tab click handling
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            for rect, label in zip(getattr(self.view, 'tab_rects', []), ['default', 'ground']):
                if rect.collidepoint(mx, my):
                    self.model.current_tab = label
                    self.view.scroll_panel.scroll_offset = 0
                    # reset selection and quantity on tab switch
                    self.model.selected_item = None
                    self.model.selected_index = None
                    self.model.quantity = 1
                    return True
        # Click events
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            # Select item
            # Compute scroll area for items
            # (based on panel position, tab height, and visible item count)
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
                    # seleccionar en panel
                    self.controller.select_item(item)
                    if self.model.current_tab == 'ground':
                        self.model.selected_index = idx
                    else:
                        self.model.selected_index = None
                    return True
            # Confirm add to inventory
            btn = self.view.add_button_rect
            if btn and btn.collidepoint(mx, my):
                # Actualizar cantidad desde el input antes de confirmar
                try:
                    self.model.quantity = int(self.view.text_input.text)
                except (ValueError, TypeError):
                    self.model.quantity = 1
                item, qty = self.controller.confirm()
                # agregar en grid
                self.grid_controller.select_item(item)
                self.grid_controller.confirm_quantity(qty)
                # Reset text input state
                self.view.text_input.active = False
                return True
        return False
