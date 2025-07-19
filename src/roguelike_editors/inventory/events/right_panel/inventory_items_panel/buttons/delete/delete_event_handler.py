import pygame

class DeleteEventHandler:
    """
    Event handler para flujo de eliminar ítems en el grid.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.editor_controller.view

    def handle(self, event):
        # Manejo de foco en input de cantidad
        if self.model.show_delete_mode:
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                dq_input = self.view.grid_view.delete_qty_input
                if hasattr(dq_input, 'last_rect') and dq_input.last_rect and dq_input.last_rect.collidepoint(event.pos):
                    dq_input.activate(initial_text=str(self.model.delete_quantity), select_all=True)
                    return True
            if self.view.grid_view.delete_qty_input.handle_event(event):
                try:
                    self.model.delete_quantity = int(self.view.grid_view.delete_qty_input.text)
                except ValueError:
                    self.model.delete_quantity = 1
                return True

        # Toggle delete mode y acción
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            # Toggle delete mode
            if getattr(self.view, 'delete_item_rect', None) and self.view.delete_item_rect.collidepoint(mx, my):
                self.model.show_delete_mode = not self.model.show_delete_mode
                self.model.show_delete_quantity_input = self.model.show_delete_mode
                self.model.delete_quantity = 1
                dq_input = self.view.grid_view.delete_qty_input
                dq_input.text = str(self.model.delete_quantity)
                dq_input.cursor = len(dq_input.text)
                dq_input.selection_start = dq_input.cursor
                dq_input.selection_end = dq_input.cursor
                dq_input.active = False
                return True
            # Cancelar al hacer click en el input
            dq_rect = getattr(self.view.grid_view, 'delete_qty_input_rect', None)
            if dq_rect and dq_rect.collidepoint(mx, my):
                return True
            if self.model.show_delete_mode:
                slots = self.view.grid_view._get_slots(self.controller.editor_controller.model)
                idx = self.view.grid_view.get_slot_index((mx, my), self.view.left_panel_rect, len(slots))
                if idx is not None and slots[idx]:
                    self.controller.delete_item(idx, self.model.delete_quantity)
                self.model.show_delete_mode = False
                self.model.show_delete_quantity_input = False
                return True

        return False
