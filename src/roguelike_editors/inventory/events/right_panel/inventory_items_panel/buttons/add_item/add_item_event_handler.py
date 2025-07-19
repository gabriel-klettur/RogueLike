import pygame

class AddItemEventHandler:
    """
    Event handler para flujo de añadir ítems en el grid.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.editor_controller.view

    def handle(self, event):
        # Detectar click en 'Add Item'
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            if hasattr(self.view, 'add_item_rect') and self.view.add_item_rect and not self.model.show_item_list and self.view.add_item_rect.collidepoint(mx, my):
                self.controller.start_add_item()
                return True
        # Manejo arrastre del panel de ítems
        if self.model.show_item_list:
            # Drag header
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                mx, my = event.pos
                header = getattr(self.view, 'item_list_header_rect', None)
                if header and header.collidepoint(mx, my):
                    self.view.item_list_dragging = True
                    self.view.item_list_drag_start_pos = pygame.Vector2(mx, my) - self.view.item_list_drag_offset
                    return True
            if event.type == pygame.MOUSEMOTION and getattr(self.view, 'item_list_dragging', False):
                mx, my = event.pos
                self.view.item_list_drag_offset = pygame.Vector2(mx, my) - self.view.item_list_drag_start_pos
                return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and getattr(self.view, 'item_list_dragging', False):
                self.view.item_list_dragging = False
                return True
            # Scroll y selección de lista
            if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                panel = getattr(self.view, 'item_list_scroll_panel', None)
                if panel and panel.handle_event(event):
                    return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                panel_rect = getattr(self.view, 'item_list_panel_rect', None)
                panel = getattr(self.view, 'item_list_scroll_panel', None)
                if panel_rect and panel_rect.collidepoint(mx, my) and panel:
                    line_h = self.view.font.get_linesize()
                    idx = (my - panel_rect.y + panel.scroll_offset) // line_h
                    items = panel.items
                    if 0 <= idx < len(items):
                        self.controller.select_item(items[idx])
                else:
                    self.model.show_item_list = False
                return True
        # Flujo de ingreso de cantidad
        if self.model.show_quantity_input:
            # Clic en botón 'Add to Inventory'
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                btn_rect = getattr(self.view, 'add_to_inventory_button_rect', None)
                if btn_rect and btn_rect.collidepoint(mx, my):
                    self.controller.confirm_quantity(self.model.quantity)
                    return True
            if event.type == pygame.KEYDOWN:
                if event.key == pygame.K_RETURN:
                    self.controller.confirm_quantity(self.model.quantity)
                    return True
                elif event.key == pygame.K_BACKSPACE:
                    qty_str = str(self.model.quantity)
                    qty_str = qty_str[:-1] if len(qty_str) > 1 else '1'
                    self.model.quantity = int(qty_str)
                    return True
                elif hasattr(event, 'unicode') and event.unicode.isdigit():
                    self.model.quantity = self.model.quantity * 10 + int(event.unicode)
                    return True
                elif event.key == pygame.K_ESCAPE:
                    self.model.show_quantity_input = False
                    self.model.show_item_list = False
                    return True
            # Consumir otros eventos mientras ingresa cantidad
            return True
        return False
