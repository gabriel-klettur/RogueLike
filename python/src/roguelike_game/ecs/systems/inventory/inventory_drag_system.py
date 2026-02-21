import os
import uuid
import math
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_game.ecs.utils.map_utils import get_zone_offset
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
import roguelike_game.config.players_config as players_config

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

        # Iniciar drag instantáneo (sin hold) cuando se presiona sobre un slot válido
        if self.dragging_idx is None and mouse_pressed and not self.prev_mouse:
            if panel.collidepoint(mouse_x, mouse_y):
                rel_x = mouse_x - panel.x - padding
                rel_y = mouse_y - panel.y - padding
                if rel_x >= 0 and rel_y >= 0:
                    col = int(rel_x // (slot_w + padding))
                    row = int(rel_y // (slot_h + padding))
                    idx = row * cols + col
                    # Validar que el click ocurrió dentro del rect del slot (no en padding)
                    sx = panel.x + padding + col * (slot_w + padding)
                    sy = panel.y + padding + row * (slot_h + padding)
                    slot_rect = pygame.Rect(sx, sy, slot_w, slot_h)
                    inv = world.components.get('InventoryComponent', {}).get(player)
                    if inv and 0 <= idx < len(inv.slots) and inv.slots[idx] and slot_rect.collidepoint(mouse_x, mouse_y):
                        self.dragging_idx = idx
                        # limpiar cualquier estado de hold anterior por seguridad
                        self.potential_drag_idx = None
                        self.drag_press_time = None

        # Sin confirmación por hold: arrastre ya se inicia instantáneamente

        # Soltar drag: manejar inventario→inventario y mapa
        if self.dragging_idx is not None and not mouse_pressed and self.prev_mouse:
            # reset potential drag on release before threshold
            self.potential_drag_idx = None
            self.drag_press_time = None
            # Inventario → Inventario: mover/stackear al soltar dentro del panel
            if panel.collidepoint(mouse_x, mouse_y):
                inv_ui = next((s for s in getattr(world, 'render_systems', []) if isinstance(s, InventoryUISystem)), None)
                inv = world.components.get('InventoryComponent', {}).get(player)
                if inv_ui and inv and 0 <= self.dragging_idx < len(inv.slots):
                    # Calcular slot destino bajo el mouse y validar dentro de su rect (no padding)
                    cols = getattr(inv_ui, 'GRID_COLS', 5)
                    padding = getattr(inv_ui, 'PADDING', 10)
                    size = getattr(inv_ui, 'SLOT_SIZE', 64)
                    rel_x = mouse_x - panel.x - padding
                    rel_y = mouse_y - panel.y - padding
                    changed = False
                    if rel_x >= 0 and rel_y >= 0:
                        col = int(rel_x // (size + padding))
                        row = int(rel_y // (size + padding))
                        dst_idx = row * cols + col
                        # Validar posicion exacta dentro del slot
                        sx = panel.x + padding + col * (size + padding)
                        sy = panel.y + padding + row * (size + padding)
                        slot_rect = pygame.Rect(sx, sy, size, size)
                        if 0 <= dst_idx < len(inv.slots) and slot_rect.collidepoint(mouse_x, mouse_y):
                            src_idx = self.dragging_idx
                            if dst_idx != src_idx:
                                src_stack = inv.slots[src_idx]
                                dst_stack = inv.slots[dst_idx]
                                if src_stack:
                                    # Movimiento a vacío
                                    if dst_stack is None:
                                        inv.slots[dst_idx] = src_stack
                                        inv.slots[src_idx] = None
                                        changed = True
                                    # Stacking si mismo item y permitido
                                    elif dst_stack.item_id == src_stack.item_id:
                                        # Consultar reglas de stack desde UI items
                                        model = getattr(inv_ui, 'items', {}).get(src_stack.item_id)
                                        stackable = getattr(model, 'stackable', False) if model else False
                                        max_stack = getattr(model, 'max_stack', None) if model else None
                                        if stackable:
                                            if max_stack is None:
                                                # Sin límite explícito
                                                dst_stack.quantity += src_stack.quantity
                                                inv.slots[src_idx] = None
                                                changed = True
                                            else:
                                                # Respetar capacidad máxima
                                                cap = int(max_stack) - int(dst_stack.quantity)
                                                if cap > 0:
                                                    move_qty = min(cap, int(src_stack.quantity))
                                                    dst_stack.quantity += move_qty
                                                    src_stack.quantity -= move_qty
                                                    changed = True
                                                    if src_stack.quantity <= 0:
                                                        inv.slots[src_idx] = None
                                    else:
                                        # Ítem distinto: intercambiar (swap)
                                        inv.slots[dst_idx], inv.slots[src_idx] = src_stack, dst_stack
                                        changed = True
                    if changed:
                        # Persistir inventario tras operación
                        persist_sys = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, InventoryPickupSystem)), None)
                        if persist_sys:
                            persist_sys._persist_inventory(player, inv)
                # Finalizar drag en cualquier caso dentro del panel
                self.dragging_idx = None
            else:
                inv = world.components.get('InventoryComponent', {}).get(player)
                if inv and 0 <= self.dragging_idx < len(inv.slots):
                    stack = inv.slots[self.dragging_idx]
                    if stack:
                        # Soltar en mapa con clamp por rango desde el jugador
                        world_x = mouse_x / camera.zoom + camera.offset_x
                        world_y = mouse_y / camera.zoom + camera.offset_y
                        # Centro del jugador y rango por clase
                        jcx = jcy = None
                        rng = None
                        try:
                            ppos = world.components.get('Position', {}).get(player)
                            pspr = world.components.get('Sprite', {}).get(player)
                            if ppos and pspr:
                                pscale_comp = world.components.get('Scale', {}).get(player)
                                pscale = pscale_comp.scale if pscale_comp else 1.0
                                jw, jh = pspr.image.get_size()
                                jw = jw * pscale
                                jh = jh * pscale
                                jcx = ppos.x + jw * 0.5
                                jcy = ppos.y + jh * 0.5
                                # Obtener rango por clase
                                cls = getattr(getattr(world, 'state', None), 'current_player_class', None) or players_config.PLAYER_CFG.get("DEFAULT_CLASS")
                                stats = players_config.PLAYER_STATS.get(cls, {}) or {}
                                rng = float(stats.get('drag_drop_range', 128))
                                dx = world_x - jcx
                                dy = world_y - jcy
                                dist = math.hypot(dx, dy)
                                if dist > rng and dist > 0:
                                    scale = rng / dist
                                    world_x = jcx + dx * scale
                                    world_y = jcy + dy * scale
                        except Exception:
                            jcx = jcy = rng = None
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
                            # Asegurar que el centro del tile esté dentro del rango si conocemos jcx/jcy
                            if jcx is not None and rng is not None:
                                tile_cx = (cx + 0.5) * TILE_SIZE
                                tile_cy = (cy + 0.5) * TILE_SIZE
                                if math.hypot(tile_cx - jcx, tile_cy - jcy) > rng:
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
        # Si se suelta antes del umbral sin haber iniciado drag, limpiar estado potencial
        if self.dragging_idx is None and not mouse_pressed and self.prev_mouse:
            self.potential_drag_idx = None
            self.drag_press_time = None
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
