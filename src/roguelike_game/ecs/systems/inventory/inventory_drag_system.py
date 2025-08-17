import os
import uuid
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_game.ecs.utils.map_utils import get_zone_offset
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
            drop_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        self.drop_manager = ItemDropManager(drop_path)
        self.dragging_idx = None
        self.prev_mouse = False
        # Hold-drag support: record potential drag
        self.drag_press_time = None
        self.potential_drag_idx = None
        self.drag_hold_threshold = 500  # ms

    @benchmark(lambda self: self.perf_log, "InventoryDragSystem.update")
    def update(self, world, camera=None):
        # Procesar solo para jugador
        player = getattr(world, 'player_entity', None)
        if player is None:
            return
        # Detectar ratón
        mouse_pressed = pygame.mouse.get_pressed()[0]
        mouse_x, mouse_y = pygame.mouse.get_pos()
        now = pygame.time.get_ticks()
        # Ubicar UI de inventario
        inv_ui = next((s for s in getattr(world, 'render_systems', []) if isinstance(s, InventoryUISystem)), None)
        if not inv_ui or not inv_ui.visible or not inv_ui.panel_rect:
            # reset drag si estaba activo y salir
            self.dragging_idx = None
            self.potential_drag_idx = None
            self.drag_press_time = None
            self.prev_mouse = mouse_pressed
            return

        panel = inv_ui.panel_rect
        cols = 5
        padding = 10
        slot_w, slot_h = 64, 64

        # Iniciar potencial drag tras mantener presionado 0.5s
        if self.dragging_idx is None and mouse_pressed and not self.prev_mouse:
            if panel.collidepoint(mouse_x, mouse_y):
                rel_x = mouse_x - panel.x - padding
                rel_y = mouse_y - panel.y - padding
                col = int(rel_x // (slot_w + padding))
                row = int(rel_y // (slot_h + padding))
                idx = row * cols + col
                inv = world.components.get('InventoryComponent', {}).get(player)
                if inv and 0 <= idx < len(inv.slots) and inv.slots[idx]:
                    # start timing potential drag
                    self.drag_press_time = now
                    self.potential_drag_idx = idx

        # Confirmar arrastre tras 0.5s de click
        if self.dragging_idx is None and self.potential_drag_idx is not None and mouse_pressed:
            if now - self.drag_press_time >= self.drag_hold_threshold:
                self.dragging_idx = self.potential_drag_idx
                self.potential_drag_idx = None

        # Soltar drag: crear drop en mapa solo si se suelta fuera del panel
        if self.dragging_idx is not None and not mouse_pressed and self.prev_mouse:
            # reset potential drag on release before threshold
            self.potential_drag_idx = None
            self.drag_press_time = None
            # Cancelar drop si el release ocurre dentro del panel (click)
            if panel.collidepoint(mouse_x, mouse_y):
                self.dragging_idx = None
            else:
                inv = world.components.get('InventoryComponent', {}).get(player)
                if inv and 0 <= self.dragging_idx < len(inv.slots):
                    stack = inv.slots[self.dragging_idx]
                    if stack:
                        # Soltar en mapa usando no-colisión por tile
                        world_x = mouse_x / camera.zoom + camera.offset_x
                        world_y = mouse_y / camera.zoom + camera.offset_y
                        g_tx = int(world_x // TILE_SIZE)
                        g_ty = int(world_y // TILE_SIZE)
                        zone_id = get_zone_for_tile(g_tx, g_ty)
                        offx, offy = get_zone_offset(zone_id)
                        occupied = self._collect_occupied_tiles(world, zone_id, offx, offy)
                        map_manager = getattr(world, 'map_manager', None)
                        placed_local = None
                        for cx, cy in self._iter_spiral_tiles(g_tx, g_ty, 12):
                            l_tx, l_ty = cx - offx, cy - offy
                            if (l_tx, l_ty) in occupied:
                                continue
                            if map_manager and not map_manager.is_walkable(cx, cy):
                                continue
                            placed_local = (l_tx, l_ty)
                            break
                        if placed_local is None:
                            placed_local = (g_tx - offx, g_ty - offy)
                        drop_id = str(uuid.uuid4())
                        self.drop_manager.create_drop(
                            drop_id, stack.item_id, stack.quantity, zone_id,
                            tile={'x': placed_local[0], 'y': placed_local[1]}
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

    def _collect_occupied_tiles(self, world, zone_id: str, offx: int, offy: int):
        occupied = set()
        try:
            drops = self.drop_manager._data or {}
            for _, data in drops.items():
                if data.get('zone_id') != zone_id:
                    continue
                if 'tile' in data:
                    lt = data['tile']
                    occupied.add((int(lt['x']), int(lt['y'])))
                elif 'position' in data:
                    pos = data['position']
                    gtx = int(pos['x'] // TILE_SIZE)
                    gty = int(pos['y'] // TILE_SIZE)
                    occupied.add((gtx - offx, gty - offy))
        except Exception:
            pass
        comps = getattr(world, 'components', {})
        phys = comps.get('PhysicalItemComponent', {})
        positions = comps.get('Position', {})
        for deid, pic in list(phys.items()):
            try:
                if getattr(pic, 'zone_id', None) != zone_id:
                    continue
                p = positions.get(deid)
                if not p:
                    continue
                gtx = int(p.x // TILE_SIZE)
                gty = int(p.y // TILE_SIZE)
                occupied.add((gtx - offx, gty - offy))
            except Exception:
                continue
        return occupied

    def _iter_spiral_tiles(self, cx: int, cy: int, max_radius: int):
        """Yield tile coordinates in an outward square spiral from (cx, cy)."""
        yield (cx, cy)
        for r in range(1, max_radius + 1):
            x0, x1 = cx - r, cx + r
            y0, y1 = cy - r, cy + r
            # top and bottom edges
            for x in range(x0, x1 + 1):
                yield (x, y0)
                yield (x, y1)
            # left and right edges (excluding corners to avoid duplicates)
            for y in range(y0 + 1, y1):
                yield (x0, y)
                yield (x1, y)
