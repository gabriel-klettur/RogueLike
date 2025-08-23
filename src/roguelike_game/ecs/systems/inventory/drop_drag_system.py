import os
import math
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_ui.ui_blocker import is_blocked
import roguelike_game.config.players_config as players_config
from roguelike_game.ecs.components.item_models import ItemStack

import logging
logger = logging.getLogger(__name__)

class DropDragSystem:
    """
    Sistema para arrastrar drops en el mapa con click-and-hold.
    Actualiza la posición en ECS y persiste en inventory_map.json.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        self.drop_manager = ItemDropManager(path)
        self.dragging_eid = None
        self.offset_x = 0
        self.offset_y = 0
        self.prev_mouse = False
        self.potential_drag_eid = None
        self.drag_press_time = None
        self.drag_hold_threshold = 250  # ms, ground pickup hold time (half of previous 500ms)
        # Seguimiento de hover sobre slot del inventario durante el drag
        self.hover_slot_idx = None
        self.hover_start_time = None
        self.hover_fill_threshold = 300  # ms para el efecto visual de relleno
        # Guardar origen del drag en coords de mundo para validar rango al soltar sobre jugador
        self.drag_origin = None

    def _get_pickup_range(self, world) -> float:
        """Return per-class pickup range (pixels). Fallback to 128 when missing."""
        try:
            cls = None
            state = getattr(world, 'state', None)
            if state is not None:
                cls = getattr(state, 'current_player_class', None)
            if not cls:
                cls = players_config.PLAYER_CFG.get("DEFAULT_CLASS")
            stats = players_config.PLAYER_STATS.get(cls, {}) or {}
            rng = stats.get("drag_drop_range")
            return float(rng) if rng is not None else 128.0
        except Exception:
            return 128.0

    @benchmark(lambda self: self.perf_log, "DropDragSystem.update")
    def update(self, world, camera):
        comps = world.components
        mouse_buttons = pygame.mouse.get_pressed()
        # Use RMB while Items editor is visible; otherwise, default to LMB
        use_rmb = False
        try:
            use_rmb = (
                hasattr(world, 'state') and getattr(world.state, 'item_editor_state', None)
                and bool(world.state.item_editor_state.visible)
            )
        except Exception:
            use_rmb = False
        active_pressed = mouse_buttons[2] if use_rmb else mouse_buttons[0]
        now = pygame.time.get_ticks()
        mouse_x, mouse_y = pygame.mouse.get_pos()
        # Convertir mouse screen a world coords
        world_x = mouse_x / camera.zoom + camera.offset_x
        world_y = mouse_y / camera.zoom + camera.offset_y


        # Soltar botón: cancelar potencial o finalizar arrastre
        if not active_pressed:
            if self.dragging_eid is None and self.potential_drag_eid is not None:
                self.potential_drag_eid = None
                self.drag_press_time = None
            if self.dragging_eid is None:
                self.prev_mouse = active_pressed
                return
            # Detectar fin de drag: caer en UI o actualizar JSON
            ui_sys = next((s for s in getattr(world, 'render_systems', []) if isinstance(s, InventoryUISystem)), None)
            if (
                ui_sys and ui_sys.visible and ui_sys.panel_rect and ui_sys.panel_rect.collidepoint(mouse_x, mouse_y)
                and not (
                    hasattr(world, 'state') and getattr(world.state, 'item_editor_state', None)
                    and world.state.item_editor_state.visible
                )
            ):
                # Calcular índice de slot exacto bajo el mouse
                panel = ui_sys.panel_rect
                cols = getattr(ui_sys, 'GRID_COLS', 5)
                padding = getattr(ui_sys, 'PADDING', 10)
                size = getattr(ui_sys, 'SLOT_SIZE', 64)
                rel_x = mouse_x - panel.x - padding
                rel_y = mouse_y - panel.y - padding
                drop_handled = False
                if rel_x >= 0 and rel_y >= 0:
                    col = int(rel_x // (size + padding))
                    row = int(rel_y // (size + padding))
                    idx = row * cols + col
                    # Validar mouse dentro del rect del slot calculado
                    sx = panel.x + padding + col * (size + padding)
                    sy = panel.y + padding + row * (size + padding)
                    slot_rect = pygame.Rect(sx, sy, size, size)
                    if slot_rect.collidepoint(mouse_x, mouse_y):
                        phys = comps['PhysicalItemComponent'][self.dragging_eid]
                        player = getattr(world, 'player_entity', None)
                        inv_comp = comps.get('InventoryComponent', {}).get(player) if player else None
                        if inv_comp and 0 <= idx < len(inv_comp.slots):
                            curr = inv_comp.slots[idx]
                            if curr is None:
                                inv_comp.slots[idx] = ItemStack(phys.item_id, phys.quantity)
                                drop_handled = True
                            elif curr.item_id == phys.item_id:
                                curr.quantity += phys.quantity
                                drop_handled = True
                        if drop_handled:
                            pickup_sys = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, InventoryPickupSystem)), None)
                            if pickup_sys and player and inv_comp:
                                pickup_sys._persist_inventory(player, inv_comp)
                            self.drop_manager.pick_up(phys.drop_id)
                            world.remove_entity(self.dragging_eid)
                            self.dragging_eid = None
                            # limpiar hover visual tras finalizar
                            self.hover_slot_idx = None
                            self.hover_start_time = None
                            return
                # Si no se soltó sobre un slot válido u ocupado con distinto item, no recoger: continuar flujo normal
            # Detectar fin de drag: caer sobre el jugador -> recoger al inventario
            player = getattr(world, 'player_entity', None)
            if player is not None:
                ppos = comps.get('Position', {}).get(player)
                pspr = comps.get('Sprite', {}).get(player)
                if ppos and pspr:
                    pscale_comp = comps.get('Scale', {}).get(player)
                    pscale = pscale_comp.scale if pscale_comp else 1.0
                    pw, ph = pspr.image.get_size()
                    pw = int(pw * pscale * camera.zoom)
                    ph = int(ph * pscale * camera.zoom)
                    psx, psy = camera.apply((ppos.x, ppos.y))
                    prect = pygame.Rect(psx, psy, pw, ph)
                    # Inflar hitbox para facilitar el drop sobre el jugador
                    prect = prect.inflate(12, 12)
                    if prect.collidepoint(mouse_x, mouse_y):
                        # Validar que el ítem estaba dentro de rango al iniciar el drag
                        in_range = True
                        try:
                            rng = self._get_pickup_range(world)
                            # Si tenemos origen del drag, usarlo para validar contra centro del jugador
                            if self.drag_origin is not None:
                                # centro jugador (coords de mundo)
                                j_w, j_h = pspr.image.get_size()
                                j_w = j_w * (comps.get('Scale', {}).get(player).scale if comps.get('Scale', {}).get(player) else 1.0)
                                j_h = j_h * (comps.get('Scale', {}).get(player).scale if comps.get('Scale', {}).get(player) else 1.0)
                                jcx = ppos.x + j_w * 0.5
                                jcy = ppos.y + j_h * 0.5
                                dx = (self.drag_origin[0] - jcx)
                                dy = (self.drag_origin[1] - jcy)
                                in_range = math.hypot(dx, dy) <= rng
                        except Exception:
                            in_range = True
                        if in_range:
                            phys = comps['PhysicalItemComponent'][self.dragging_eid]
                            inv_comp = comps.get('InventoryComponent', {}).get(player)
                            if inv_comp:
                                inv_comp.add(phys.item_id, phys.quantity)
                                pickup_sys = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, InventoryPickupSystem)), None)
                                if pickup_sys:
                                    pickup_sys._persist_inventory(player, inv_comp)
                            self.drop_manager.pick_up(phys.drop_id)
                            world.remove_entity(self.dragging_eid)
                            self.dragging_eid = None
                            self.drag_origin = None
                            return
            # Clampear a rango máximo desde el jugador y actualizar drop en JSON
            phys = comps['PhysicalItemComponent'][self.dragging_eid]
            pos = comps['Position'][self.dragging_eid]
            try:
                player = getattr(world, 'player_entity', None)
                if player is not None:
                    ppos = comps.get('Position', {}).get(player)
                    pspr = comps.get('Sprite', {}).get(player)
                    if ppos and pspr:
                        pscale_comp = comps.get('Scale', {}).get(player)
                        pscale = pscale_comp.scale if pscale_comp else 1.0
                        jw, jh = pspr.image.get_size()
                        jw = jw * pscale
                        jh = jh * pscale
                        jcx = ppos.x + jw * 0.5
                        jcy = ppos.y + jh * 0.5
                        rng = self._get_pickup_range(world)
                        dx = pos.x - jcx
                        dy = pos.y - jcy
                        dist = math.hypot(dx, dy)
                        if dist > rng and dist > 0:
                            scale = rng / dist
                            pos.x = jcx + dx * scale
                            pos.y = jcy + dy * scale
            except Exception:
                pass
            logger.debug(f"[DropDragSystem][DEBUG] Updating drop {phys.drop_id} to position ({pos.x:.2f},{pos.y:.2f})")
            self.drop_manager.update_drop(phys.drop_id, position=pos)
            self.dragging_eid = None
            self.drag_origin = None
            # limpiar hover visual tras finalizar drag
            self.hover_slot_idx = None
            self.hover_start_time = None
            return            

        # Si botón presionado pero sin drag activo: tras el hold, recoger directamente al inventario
        if self.dragging_eid is None:
            hovered = None
            max_layer = -float('inf')
            # No iniciar drag si el cursor está sobre un panel UI registrado
            if is_blocked(mouse_x, mouse_y):
                self.prev_mouse = active_pressed
                return
            for eid in world.get_entities_in_camera(camera, 'PhysicalItemComponent', 'Sprite', 'Position', 'ZLayer'):
                pos2 = comps['Position'][eid]
                sprite = comps['Sprite'][eid]
                scale_comp = comps.get('Scale', {}).get(eid)
                scale = scale_comp.scale if scale_comp else 1.0
                w, h = sprite.image.get_size()
                w = int(w * scale * camera.zoom)
                h = int(h * scale * camera.zoom)
                sx, sy = camera.apply((pos2.x, pos2.y))
                rect = pygame.Rect(sx, sy, w, h)
                if rect.collidepoint(mouse_x, mouse_y):
                    layer = comps['ZLayer'][eid].layer
                    if layer >= max_layer:
                        hovered = eid
                        max_layer = layer
            if hovered is not None:
                if not self.prev_mouse and active_pressed:
                    self.drag_press_time = now
                    self.potential_drag_eid = hovered
                elif self.potential_drag_eid == hovered and active_pressed and now - self.drag_press_time >= self.drag_hold_threshold:
                    # Al completar el hold, recoger directamente al inventario (sin iniciar drag)
                    allow_pickup = True
                    try:
                        player = getattr(world, 'player_entity', None)
                        if player is not None:
                            ppos = comps.get('Position', {}).get(player)
                            pspr = comps.get('Sprite', {}).get(player)
                            if ppos and pspr:
                                pscale_comp = comps.get('Scale', {}).get(player)
                                pscale = pscale_comp.scale if pscale_comp else 1.0
                                pw, ph = pspr.image.get_size()
                                pw = pw * pscale
                                ph = ph * pscale
                                pcx = ppos.x + pw * 0.5
                                pcy = ppos.y + ph * 0.5
                                dpos = comps['Position'][hovered]
                                dspr = comps['Sprite'][hovered]
                                dscale_comp = comps.get('Scale', {}).get(hovered)
                                dscale = dscale_comp.scale if dscale_comp else 1.0
                                dw, dh = dspr.image.get_size()
                                dw = dw * dscale
                                dh = dh * dscale
                                dcx = dpos.x + dw * 0.5
                                dcy = dpos.y + dh * 0.5
                                rng = self._get_pickup_range(world)
                                allow_pickup = (math.hypot(dcx - pcx, dcy - pcy) <= rng)
                    except Exception:
                        allow_pickup = True
                    if allow_pickup:
                        phys = comps['PhysicalItemComponent'][hovered]
                        player = getattr(world, 'player_entity', None)
                        inv_comp = comps.get('InventoryComponent', {}).get(player) if player else None
                        if inv_comp:
                            inv_comp.add(phys.item_id, phys.quantity)
                            pickup_sys = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, InventoryPickupSystem)), None)
                            if pickup_sys:
                                pickup_sys._persist_inventory(player, inv_comp)
                        self.drop_manager.pick_up(phys.drop_id)
                        world.remove_entity(hovered)
                        # limpiar estados tras recoger
                        self.potential_drag_eid = None
                        self.drag_press_time = None
                        self.drag_origin = None
                        self.hover_slot_idx = None
                        self.hover_start_time = None
                        self.prev_mouse = active_pressed
                        return
                self.prev_mouse = active_pressed
                return

        # Drag activo: actualizar posición componente
        # Verificar que el componente Position exista (puede haber sido eliminado)
        pos_store = comps.get('Position', {})
        pos_comp = pos_store.get(self.dragging_eid)
        if not pos_comp:
            # Cancelar drag si la entidad ya no tiene posición
            self.dragging_eid = None
            return
        pos_comp.x = world_x + self.offset_x
        pos_comp.y = world_y + self.offset_y
        # Actualizar hover sobre inventario para feedback visual
        ui_sys = next((s for s in getattr(world, 'render_systems', []) if isinstance(s, InventoryUISystem)), None)
        if ui_sys and ui_sys.visible and ui_sys.panel_rect:
            mx, my = mouse_x, mouse_y
            panel = ui_sys.panel_rect
            if panel.collidepoint(mx, my):
                cols = 5
                padding = 10
                size = 64
                rel_x = mx - panel.x - padding
                rel_y = my - panel.y - padding
                if rel_x >= 0 and rel_y >= 0:
                    col = int(rel_x // (size + padding))
                    row = int(rel_y // (size + padding))
                    idx = row * cols + col
                    # Validar que el cursor está dentro del rect del slot calculado
                    sx = panel.x + padding + col * (size + padding)
                    sy = panel.y + padding + row * (size + padding)
                    slot_rect = pygame.Rect(sx, sy, size, size)
                    if slot_rect.collidepoint(mx, my):
                        if self.hover_slot_idx != idx:
                            self.hover_slot_idx = idx
                            self.hover_start_time = now
                    else:
                        self.hover_slot_idx = None
                        self.hover_start_time = None
                else:
                    self.hover_slot_idx = None
                    self.hover_start_time = None
            else:
                self.hover_slot_idx = None
                self.hover_start_time = None
        else:
            self.hover_slot_idx = None
            self.hover_start_time = None
