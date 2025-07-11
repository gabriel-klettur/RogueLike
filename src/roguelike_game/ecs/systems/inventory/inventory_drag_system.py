import os
import uuid
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem

class InventoryDragSystem:
    """
    Sistema para arrastrar items del inventario al mapa (drag&drop).
    """
    def __init__(self, perf_log=None, drop_path=None):
        self.perf_log = perf_log
        if drop_path is None:
            drop_path = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_map.json')
        self.drop_manager = ItemDropManager(drop_path)
        self.dragging_idx = None
        self.prev_mouse = False

    @benchmark(lambda self: self.perf_log, "InventoryDragSystem.update")
    def update(self, world, camera=None):
        # Procesar solo para jugador
        player = getattr(world, 'player_entity', None)
        if player is None:
            return
        # Detectar ratón
        mouse_pressed = pygame.mouse.get_pressed()[0]
        mouse_x, mouse_y = pygame.mouse.get_pos()
        # Ubicar UI de inventario
        inv_ui = next((s for s in getattr(world, 'render_systems', []) if isinstance(s, InventoryUISystem)), None)
        if not inv_ui or not inv_ui.visible or not inv_ui.panel_rect:
            # reset drag si estaba activo
            self.dragging_idx = None
            self.prev_mouse = mouse_pressed
            return
        panel = inv_ui.panel_rect
        cols = 5
        padding = 10
        slot_w, slot_h = 64, 64
        # Iniciar drag al pulsar sobre un slot con item
        if self.dragging_idx is None and mouse_pressed and not self.prev_mouse:
            if panel.collidepoint(mouse_x, mouse_y):
                rel_x = mouse_x - panel.x - padding
                rel_y = mouse_y - panel.y - padding
                col = int(rel_x // (slot_w + padding))
                row = int(rel_y // (slot_h + padding))
                idx = row * cols + col
                inv = world.components.get('InventoryComponent', {}).get(player)
                if inv and 0 <= idx < len(inv.slots) and inv.slots[idx]:
                    self.dragging_idx = idx
        # Soltar drag: crear drop en mapa si cae fuera del panel
        if self.dragging_idx is not None and not mouse_pressed and self.prev_mouse:
            inv = world.components.get('InventoryComponent', {}).get(player)
            if inv and 0 <= self.dragging_idx < len(inv.slots):
                stack = inv.slots[self.dragging_idx]
                if stack:
                    # Soltar en mapa
                    world_x = mouse_x / camera.zoom + camera.offset_x
                    world_y = mouse_y / camera.zoom + camera.offset_y
                    drop_id = str(uuid.uuid4())
                    self.drop_manager.create_drop(
                        drop_id, stack.item_id, stack.quantity, None,
                        position={'x': world_x, 'y': world_y}
                    )
                    # Remover del inventario
                    inv.slots[self.dragging_idx] = None
                    # Persistir inventario
                    drop_sys = next((s for s in world.update_systems if isinstance(s, InventoryPickupSystem)), None)
                    if drop_sys:
                        drop_sys._persist_inventory(player, inv)
            # reset drag
            self.dragging_idx = None
        self.prev_mouse = mouse_pressed
