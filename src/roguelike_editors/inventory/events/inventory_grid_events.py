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
        # Detectar click en 'Add Item'
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            if hasattr(self.editor_view, 'add_item_rect') and self.editor_view.add_item_rect and not self.model.show_item_list and self.editor_view.add_item_rect.collidepoint(mx, my):
                self.controller.start_add_item()
                return True
        # Selección de ítem de la lista
        if self.model.show_item_list and event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            for rect, item_id in getattr(self.editor_view, 'item_list_rects', []):
                if rect.collidepoint(mx, my):
                    self.controller.select_item(item_id)
                    return True
            # click fuera de la lista -> cancelar selección
            self.model.show_item_list = False
            return True
        # Flujo de ingreso de cantidad
        if self.model.show_quantity_input:
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

   