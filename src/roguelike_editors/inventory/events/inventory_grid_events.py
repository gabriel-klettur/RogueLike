import pygame

class InventoryGridEventHandler:
    """
    Manejador de eventos para flujo de añadir/eliminar ítems en el grid.
    """
    def __init__(self, grid_controller):
        self.controller = grid_controller
        self.model = grid_controller.model
        self.editor_view = grid_controller.editor_controller.view

    def handle(self, event):
        """
        Retorna True si el evento fue consumido por el flujo de add/delete.
        """
        # MVC item selection panel event handling
        panel_model = getattr(self.editor_view, 'item_panel_model', None)
        panel_view = getattr(self.editor_view, 'item_panel_view', None)
        if panel_model and panel_model.show_panel and panel_view:
            # Drag header
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                if panel_view.header_rect.collidepoint(event.pos):
                    panel_model.dragging = True
                    panel_model.drag_start_pos = pygame.Vector2(event.pos) - panel_model.drag_offset
                    return True
            if event.type == pygame.MOUSEMOTION and panel_model.dragging:
                panel_model.drag_offset = pygame.Vector2(event.pos) - panel_model.drag_start_pos
                return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and panel_model.dragging:
                panel_model.dragging = False
                return True
            # Scroll wheel
            if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                if panel_view.scroll_panel.handle_event(event):
                    return True
            # Click selection or confirm
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                panel_rect = panel_view.panel_rect
                # Select item
                if panel_rect.collidepoint(mx, my):
                    line_h = self.editor_view.font.get_linesize()
                    idx = (my - panel_rect.y + panel_view.scroll_panel.scroll_offset) // line_h
                    items = panel_view.scroll_panel.items
                    if 0 <= idx < len(items):
                        self.controller.select_item(items[idx])
                    return True
                # Confirm add
                btn_rect = panel_view.add_button_rect
                if btn_rect and btn_rect.collidepoint(mx, my):
                    qty = panel_model.quantity
                    self.controller.confirm_quantity(qty)
                    panel_model.show_panel = False
                    return True
        # Detectar click en 'Add Item'
        # Detectar click en 'Add Item'
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            if hasattr(self.editor_view, 'add_item_rect') and self.editor_view.add_item_rect and not self.model.show_item_list and self.editor_view.add_item_rect.collidepoint(mx, my):
                self.controller.start_add_item()
                return True
        # Manejo arrastre del panel de ítems
        if self.model.show_item_list:
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                mx, my = event.pos
                header = getattr(self.editor_view, 'item_list_header_rect', None)
                if header and header.collidepoint(mx, my):
                    self.editor_view.item_list_dragging = True
                    self.editor_view.item_list_drag_start_pos = pygame.Vector2(mx, my) - self.editor_view.item_list_drag_offset
                    return True
            if event.type == pygame.MOUSEMOTION and getattr(self.editor_view, 'item_list_dragging', False):
                mx, my = event.pos
                self.editor_view.item_list_drag_offset = pygame.Vector2(mx, my) - self.editor_view.item_list_drag_start_pos
                return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and getattr(self.editor_view, 'item_list_dragging', False):
                self.editor_view.item_list_dragging = False
                return True
        # Manejo scroll y selección de ítem
        if self.model.show_item_list:
            # scroll con rueda
            if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                if self.editor_view.scroll_panel.handle_event(event):
                    return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                panel_rect = getattr(self.editor_view, 'item_list_panel_rect', None)
                if panel_rect and panel_rect.collidepoint(mx, my):
                    # calcular índice de ítem
                    line_h = self.editor_view.font.get_linesize()
                    idx = (my - panel_rect.y + self.editor_view.scroll_panel.scroll_offset) // line_h
                    items = self.editor_view.scroll_panel.items
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
                btn_rect = getattr(self.editor_view, 'add_to_inventory_button_rect', None)
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

   