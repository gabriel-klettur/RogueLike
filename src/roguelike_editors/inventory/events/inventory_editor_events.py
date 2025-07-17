import pygame
from types import SimpleNamespace

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
        # Recentrar cámara si estaba en enfoque de monstruo
        if self.model.camera_focus_target is not None and event.type in (pygame.KEYDOWN, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL):
            player_eid = self.world.player_entity
            pos_map = self.world.components.get('Position', {})
            if player_eid in pos_map:
                pos = pos_map[player_eid]
                self.controller.game.camera.update(SimpleNamespace(x=pos.x, y=pos.y))
            self.model.camera_focus_target = None
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
                # Reset inventory panel debug prints
                self.controller.inventory_panel_controller.debug_printed = False
                self.model.editing_side = 'active'
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
            print(f"[DEBUG InvEditor] MouseUp at {(mx, my)}")
            # Show Default Button            
            if self.view.show_default_rect and self.view.show_default_rect.collidepoint(mx, my):
                print("[DEBUG InvEditor] Show Default button clicked")
                self.model.editing_side = 'default'
                return
            # Show Active Button            
            if self.view.show_active_rect and self.view.show_active_rect.collidepoint(mx, my):
                print("[DEBUG InvEditor] Show Active button clicked")
                self.model.editing_side = 'active'
                return
            # Save Default Button            
            # Save Button
            if self.view.save_rect and self.view.save_rect.collidepoint(mx, my):
                print(f"[DEBUG InvEditor] Save button clicked (side={self.model.editing_side})")
                if self.model.editing_side == 'default':
                    self.controller._save_default()
                else:
                    self.controller._save_active()
                return