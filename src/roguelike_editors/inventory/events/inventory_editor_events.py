import pygame

class InventoryEditorEventHandler:
    """
    Manejador de eventos para el editor de inventario.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.world = controller.world
        self.view = controller.view

    def handle(self, event):
        # Toggle editor con F6
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F6:
            self.model.visible = not self.model.visible
            print(f"[DEBUG InventoryEditorController] F6 pressed, visible={self.model.visible}")
            if self.model.visible:
                # Inicializar lista de entidades
                players = list(self.world.components.get('PlayerTagComponent', {}).keys())
                npcs = list(self.world.components.get('NPCTagComponent', {}).keys())
                self.model.entities = players + npcs
                self.model.selected_eid = self.model.entities[0] if self.model.entities else None
            return
        if not self.model.visible:
            return
            


        # Mouse down
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            inv = self.world.components.get('InventoryComponent', {}).get(self.model.selected_eid)
            if inv and self.model.drag_item is None:
                slot_idx = self.view.get_slot_at_pos((mx, my), len(inv.slots))
                if slot_idx is not None and inv.slots[slot_idx]:
                    stack = inv.slots[slot_idx]
                    self.model.drag_item = stack
                    self.model.drag_slot = slot_idx
                    inv.slots[slot_idx] = None
        # Mouse up
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            inv = self.world.components.get('InventoryComponent', {}).get(self.model.selected_eid)
            if inv and self.model.drag_item is not None:
                slot_idx = self.view.get_slot_at_pos((mx, my), len(inv.slots))
                if slot_idx is not None and inv.slots[slot_idx] is None:
                    inv.slots[slot_idx] = self.model.drag_item
                else:
                    inv.slots[self.model.drag_slot] = self.model.drag_item
                self.model.drag_item = None
                self.model.drag_slot = None
            # Botones
            if self.view.show_default_rect and self.view.show_default_rect.collidepoint(mx, my):
                self.model.editing_side = 'default'
                return
            if self.view.show_active_rect and self.view.show_active_rect.collidepoint(mx, my):
                self.model.editing_side = 'active'
                return
            if self.view.save_default_rect and self.view.save_default_rect.collidepoint(mx, my):
                self.controller._save_default()
                return
            if self.view.save_active_rect and self.view.save_active_rect.collidepoint(mx, my):
                self.controller._save_active()
                return