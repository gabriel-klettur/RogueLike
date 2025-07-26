import pygame

class DeleteEventHandler:
    """
    Event handler para flujo de eliminar ítems en el grid.
    """
    def __init__(self, controller):
        # controller is actually InventoryItemsPanelController
        self.controller = controller.delete_controller  # Access delete controller directly
        self.model = controller.model.delete  # Access delete model directly
        self.view = controller.editor_controller.view
        self.parent_controller = controller

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
                print(f"[DEBUG] Delete mode active, processing click at ({mx}, {my})")
                
                # Get slots data using the editor controller's model
                try:
                    editor_model = self.controller.editor_controller.model
                    slots = self.view.grid_view.tabs_view.get_slots_data(editor_model)
                    print(f"[DEBUG] Got {len(slots)} slots: {slots[:3]}...")  # Show first 3 slots
                except Exception as e:
                    print(f"[DEBUG] Error getting slots: {e}")
                    return True
                
                # Calculate slot index using the same logic as the grid view
                try:
                    # Get the panel rect from the left panel
                    panel_rect = getattr(self.view, 'left_panel_rect', None)
                    if not panel_rect:
                        print(f"[DEBUG] No left_panel_rect found")
                        return True
                    
                    # Calculate grid origin (same as in _draw_grid)
                    grid_origin_x = panel_rect.x + panel_rect.width + self.view.margin
                    grid_origin_y = panel_rect.y
                    
                    # Use 5 columns like the actual grid
                    cols = 5
                    slot_size = self.view.slot_size
                    margin = self.view.margin
                    
                    print(f"[DEBUG] Grid calculation: origin=({grid_origin_x},{grid_origin_y}), click=({mx},{my})")
                    
                    idx = None
                    for i in range(len(slots)):
                        col = i % cols
                        row = i // cols
                        rx = grid_origin_x + col * (slot_size + margin)
                        ry = grid_origin_y + row * (slot_size + margin)
                        rect_obj = pygame.Rect(rx, ry, slot_size, slot_size)
                        
                        if i < 3:  # Only log first few slots to avoid spam
                            print(f"[DEBUG] Slot {i}: rect=({rx},{ry},{slot_size},{slot_size})")
                        
                        if rect_obj.collidepoint(mx, my):
                            idx = i
                            break
                    
                    print(f"[DEBUG] Calculated slot index: {idx}")
                except Exception as e:
                    print(f"[DEBUG] Error calculating slot index: {e}")
                    return True
                
                if idx is not None and idx < len(slots) and slots[idx]:
                    print(f"[DEBUG] Deleting item at slot {idx}: {slots[idx]}")
                    try:
                        self.controller.delete_item(idx, self.model.delete_quantity)
                        print(f"[DEBUG] Item deleted successfully")
                        # Exit delete mode after successful deletion
                        self.model.show_delete_mode = False
                        self.model.show_delete_quantity_input = False
                    except Exception as e:
                        print(f"[DEBUG] Error deleting item: {e}")
                else:
                    print(f"[DEBUG] No valid item to delete at index {idx}")
                    # Click outside items - exit delete mode
                    self.model.show_delete_mode = False
                    self.model.show_delete_quantity_input = False
                return True

        return False
