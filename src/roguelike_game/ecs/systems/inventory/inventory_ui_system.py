import os

import pygame
import logging

from .ui_assets import load_items_and_icons
from .ui_utils import compute_panel_rect, compute_slot_rect
from .ui_render import (
    draw_panel,
    draw_tabs,
    draw_slots,
    draw_drag_ghost,
    draw_drag_destination_highlight,
    draw_map_drop_feedback,
    draw_map_drop_ghost,
    measure_tabs_total_width,
    measure_footer_height,
    draw_footer_currency,
)
from .ui_constants import (
    GRID_COLS,
    GRID_ROWS,
    INCREASE_COLOR,
    DECREASE_COLOR,
    PADDING,
    SLOT_SIZE,
    TABS_LABELS,
    CURRENCY_ITEM_IDS,
)

logger = logging.getLogger(__name__)

class InventoryUISystem:
    """
    Sistema de UI para mostrar el inventario del jugador en pantalla.
    """
    # Estilos y constantes se gestionan desde `ui_constants.py`.

    def __init__(self, perf_log=None, items_path=None):
        """
        Inicializa InventoryUISystem, carga modelos de ítems y prepara fuentes e íconos.
        """
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items, self.icon_surfaces = load_items_and_icons(items_path)
        self.visible = False
        self.panel_rect = None
        # Estado de drag
        self.dragging = False
        self.drag_offset_x = 0
        self.drag_offset_y = 0
        self.drag_start_mouse_x = 0
        self.drag_start_mouse_y = 0
        self.drag_start_offset_x = 0
        self.drag_start_offset_y = 0
        self.prev_right_pressed = False
        self.prev_left_pressed = False
        # Detección de doble clic
        self.last_click_slot_idx = None
        self.last_click_time = 0
        self.double_click_threshold = 500
        pygame.font.init()
        self.font = pygame.font.SysFont(None, 24)
        # Track de cantidades por slot para resaltar incrementos/decrementos
        self._slot_last_qty: dict[int, int] = {}
        # Map idx -> {'start': ms, 'color': (r,g,b), 'kind': 'inc'|'dec'}
        self._slot_flash: dict[int, dict] = {}
        # Cambios detectados mientras la UI está cerrada: idx -> {'color':(r,g,b), 'kind':'inc'|'dec'}
        self._pending_flash_buffer: dict[int, dict] = {}
        # Pestañas
        self.active_tab_index: int = 0
        self._tab_rects: list[pygame.Rect] = []

    # Easing y helpers de layout/dibujo ahora viven en ui_utils/ui_render.

    def _get_player_input(self, world):
        """Obtiene player_entity e InputComponent."""
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return None, None
        inp = world.components.get('InputComponent', {}).get(player_eid)
        return player_eid, inp

    def _handle_toggle(self, world):
        """
        Maneja apertura/cierre del inventario.
        Retorna True si la UI debe mostrarse.
        """
        player_eid, inp = self._get_player_input(world)
        if player_eid is None:
            return False
        if inp and getattr(inp, 'toggle_inventory', False):
            self.visible = not self.visible
            inp.toggle_inventory = False
            self.logger.debug("Inventory visibility toggled: %s", self.visible)
            return False
        return self.visible

    def _get_slots(self, world):
        """Retorna la lista de slots del jugador o None si no hay inventario."""
        player_eid, _ = self._get_player_input(world)
        inv = world.components.get('InventoryComponent', {}).get(player_eid)
        if inv is None:
            return None
        return inv.slots

    # compute_panel_rect reemplazado por ui_utils.compute_panel_rect

    def _handle_drag(self, panel_rect):
        """
        Maneja arrastre del panel con click derecho.
        Debe llamarse antes de dibujar el panel.
        """
        mouse_buttons = pygame.mouse.get_pressed()
        mouse_x, mouse_y = pygame.mouse.get_pos()
        right_pressed = mouse_buttons[2]
        if right_pressed and not self.prev_right_pressed and panel_rect.collidepoint(mouse_x, mouse_y):
            self.dragging = True
            self.logger.debug(
                "Drag started at pos=(%d,%d), offset=(%d,%d)",
                mouse_x, mouse_y, self.drag_offset_x, self.drag_offset_y,
            )
            self.drag_start_mouse_x = mouse_x
            self.drag_start_mouse_y = mouse_y
            self.drag_start_offset_x = self.drag_offset_x
            self.drag_start_offset_y = self.drag_offset_y
        elif not right_pressed and self.prev_right_pressed and self.dragging:
            self.dragging = False
            self.logger.debug("Drag ended")
        if self.dragging:
            dx = mouse_x - self.drag_start_mouse_x
            dy = mouse_y - self.drag_start_mouse_y
            self.drag_offset_x = self.drag_start_offset_x + dx
            self.drag_offset_y = self.drag_start_offset_y + dy
        self.prev_right_pressed = right_pressed

    # draw_panel reemplazado por ui_render.draw_panel

    # draw_slots reemplazado por ui_render.draw_slots

    def update(self, world, screen, camera):
        """
        Update de UI de inventario: toggle, arrastre y render.
        """
        prev_visible = self.visible
        slots = self._get_slots(world)
        if slots:
            # Detectar cambios SIEMPRE, incluso si no visible. Si no visible: bufferizar.
            try:
                now_ts = pygame.time.get_ticks()
                total_slots = GRID_COLS * GRID_ROWS
                for idx in range(total_slots):
                    stack = slots[idx] if idx < len(slots) else None
                    qty = int(getattr(stack, 'quantity', 0) or 0) if stack else 0
                    last = self._slot_last_qty.get(idx)
                    if last is None:
                        self._slot_last_qty[idx] = qty
                        continue
                    if qty != last:
                        inc = qty > last
                        color = INCREASE_COLOR if inc else DECREASE_COLOR
                        if self.visible:
                            # UI visible: disparar flash inmediato
                            self._slot_flash[idx] = {'start': int(now_ts), 'color': color, 'kind': 'inc' if inc else 'dec'}
                        else:
                            # UI cerrada: bufferizar para reproducir al abrir
                            self._pending_flash_buffer[idx] = {'color': color, 'kind': 'inc' if inc else 'dec'}
                        self._slot_last_qty[idx] = qty
                # Actualizar last_qty para slots que no entraron antes (nuevos tamaños)
                for idx in range(len(slots), total_slots):
                    if idx not in self._slot_last_qty:
                        self._slot_last_qty[idx] = 0
            except Exception:
                pass
        # Manejar toggle y visibilidad
        if not self._handle_toggle(world):
            return
        # Si se acaba de abrir, reproducir flashes pendientes con tiempo fresco
        if not prev_visible and self.visible and self._pending_flash_buffer:
            now0 = pygame.time.get_ticks()
            for idx, meta in list(self._pending_flash_buffer.items()):
                col = meta.get('color', INCREASE_COLOR)
                kind = meta.get('kind', 'inc')
                self._slot_flash[idx] = {'start': int(now0), 'color': col, 'kind': kind}
            self._pending_flash_buffer.clear()
        if not slots:
            return
        # Reservar altura para cabecera + barra de pestañas y adaptar ancho mínimo al ancho de tabs
        tabs_total_w, tab_h = measure_tabs_total_width(self.font, None, include_close=True)
        header_h = self.font.get_height()
        header_top_gap = max(2, PADDING // 4)
        # Debe coincidir con la fórmula de ui_render.draw_tabs (used_h)
        tabs_reserved_h = (header_h + header_top_gap) + (tab_h + PADDING + PADDING // 2)
        # Footer reservado (oro)
        footer_h = measure_footer_height(self.font)
        footer_reserved_h = footer_h + PADDING
        # Asegurar márgenes simétricos: forzar que (panel_w - grid_w) sea par
        grid_w = GRID_COLS * SLOT_SIZE + (GRID_COLS + 1) * PADDING
        desired_w = max(grid_w, tabs_total_w + 2 * PADDING)
        if (desired_w - grid_w) % 2 != 0:
            desired_w += 1
        min_panel_w = desired_w
        initial_rect = compute_panel_rect(
            screen,
            (self.drag_offset_x, self.drag_offset_y),
            extra_h=tabs_reserved_h + footer_reserved_h,
            min_w=min_panel_w,
        )
        self._handle_drag(initial_rect)
        panel_rect = compute_panel_rect(
            screen,
            (self.drag_offset_x, self.drag_offset_y),
            extra_h=tabs_reserved_h + footer_reserved_h,
            min_w=min_panel_w,
        )
        self.panel_rect = panel_rect
        # Panel and close button
        if draw_panel(screen, panel_rect, self.font):
            self.visible = False
            self.logger.debug("Inventory closed via close button")
            return
        # Calcular ancho de contenido de la rejilla para centrar tanto tabs como slots
        grid_w = GRID_COLS * SLOT_SIZE + (GRID_COLS + 1) * PADDING
        content_left = panel_rect.x + max(0, (panel_rect.w - grid_w) // 2)
        content_right = content_left + grid_w
        # Alinear header/tabs con el área interior de slots (sin el padding externo de la rejilla)
        tabs_bounds_left = content_left + PADDING
        tabs_bounds_right = content_right - PADDING
        # Dibujar pestañas (centradas respecto al ancho de contenido) y desplazar rejilla
        tab_rects, used_h, tabs_close_rect = draw_tabs(
            screen,
            panel_rect,
            self.font,
            self.active_tab_index,
            content_bounds=(tabs_bounds_left, tabs_bounds_right),
        )
        self._tab_rects = tab_rects
        # Centrar la rejilla (GRID_COLS x GRID_ROWS) bajo las pestañas
        grid_h = GRID_ROWS * SLOT_SIZE + (GRID_ROWS + 1) * PADDING
        remaining_h = max(0, panel_rect.h - used_h - footer_reserved_h)
        grid_origin_x = content_left
        grid_origin_y = panel_rect.y + used_h + max(0, (remaining_h - grid_h) // 2)
        # grid_panel_rect incluye el padding exterior; compute_slot_rect añadirá PADDING interno
        grid_panel_rect = pygame.Rect(
            grid_origin_x,
            grid_origin_y,
            grid_w,
            grid_h,
        )
        # Draw slots; hide dragging slot for visual clarity
        drag_sys = next((s for s in getattr(world, 'update_systems', []) if hasattr(s, 'dragging_idx')), None)
        drag_idx = getattr(drag_sys, 'dragging_idx', None) if drag_sys else None
        # Filtrar por pestaña activa (sin modificar el inventario real, sólo render)
        def _in_active_category(item_id: str) -> bool:
            model = self.items.get(item_id)
            if model is None:
                return True
            label = TABS_LABELS[self.active_tab_index] if 0 <= self.active_tab_index < len(TABS_LABELS) else ""
            if label == "Equipo":
                return bool(getattr(model, 'equip_slot', None) is not None or getattr(model, 'durability', None) is not None)
            if label == "Materiales":
                # Stackeables sin efecto, no equipables, no quest
                if getattr(model, 'effect', None) is not None:
                    return False
                if getattr(model, 'equip_slot', None) is not None or getattr(model, 'durability', None) is not None:
                    return False
                if getattr(model, 'quest_id', None) is not None:
                    return False
                return bool(getattr(model, 'stackable', False))
            if label == "Consumibles":
                return bool(getattr(model, 'effect', None) is not None)
            # Si la etiqueta no coincide, permitir por defecto
            return True

        slots_to_draw = []
        for st in slots:
            if st and not _in_active_category(st.item_id):
                slots_to_draw.append(None)
            else:
                slots_to_draw.append(st)
        if drag_idx is not None and 0 <= drag_idx < len(slots_to_draw):
            slots_to_draw[drag_idx] = None
        # Hold-to-drag feedback before drag confirmation
        highlight_idx = None
        grab_progress = 0.0
        if drag_sys and drag_idx is None:
            pot_idx = getattr(drag_sys, 'potential_drag_idx', None)
            press_time = getattr(drag_sys, 'drag_press_time', None)
            threshold = getattr(drag_sys, 'drag_hold_threshold', 500)
            if pot_idx is not None and press_time is not None and 0 <= pot_idx < len(slots):
                now = pygame.time.get_ticks()
                elapsed = max(0, now - press_time)
                highlight_idx = pot_idx
                grab_progress = min(1.0, elapsed / max(1, threshold))
        draw_slots(
            screen=screen,
            panel_rect=grid_panel_rect,
            slots=slots_to_draw,
            icon_surfaces=self.icon_surfaces,
            font=self.font,
            slot_flash=self._slot_flash,
            highlight_idx=highlight_idx,
            grab_progress=grab_progress,
        )
        # Draw slots; hide dragging slot for visual clarity
        if drag_idx is not None:
            draw_drag_ghost(screen, slots, self.icon_surfaces, drag_idx)
            draw_drag_destination_highlight(screen, grid_panel_rect, drag_idx, len(slots))
        # Map->Inventory drag feedback: overlay on hovered slot + ghost sprite
        drop_sys = next((s for s in getattr(world, 'update_systems', []) if hasattr(s, 'dragging_eid')), None)
        drop_eid = getattr(drop_sys, 'dragging_eid', None) if drop_sys else None
        if drop_eid is not None:
            try:
                hover_idx = getattr(drop_sys, 'hover_slot_idx', None)
                hover_start = getattr(drop_sys, 'hover_start_time', None)
                hover_threshold = getattr(drop_sys, 'hover_fill_threshold', 300)
                if hover_idx is not None and hover_start is not None and grid_panel_rect:
                    draw_map_drop_feedback(
                        screen=screen,
                        panel_rect=grid_panel_rect,
                        hover_idx=hover_idx,
                        hover_start=hover_start,
                        hover_threshold=hover_threshold,
                    )
            except Exception:
                pass
            comps2 = world.components
            sprite_comp = comps2.get('Sprite', {}).get(drop_eid)
            if sprite_comp:
                img2 = sprite_comp.image
                scale_comp2 = comps2.get('Scale', {}).get(drop_eid)
                scale_factor2 = camera.zoom * (scale_comp2.scale if scale_comp2 else 1.0)
                draw_map_drop_ghost(screen, img2, scale_factor2)
        now = pygame.time.get_ticks()
        left_pressed = pygame.mouse.get_pressed()[0]
        mouse_pos = pygame.mouse.get_pos()
        left_clicked = left_pressed and not self.prev_left_pressed
        self.prev_left_pressed = left_pressed

        # Footer: calcular oro total y dibujar
        total_gold = 0
        gold_icon = None
        try:
            for st in slots:
                if not st:
                    continue
                if st.item_id in CURRENCY_ITEM_IDS:
                    total_gold += int(getattr(st, 'quantity', 0) or 0)
                    if gold_icon is None:
                        gold_icon = self.icon_surfaces.get(st.item_id)
        except Exception:
            pass
        footer_top_y = panel_rect.bottom - (PADDING + footer_h)
        footer_rect = draw_footer_currency(
            screen=screen,
            content_bounds=(tabs_bounds_left, tabs_bounds_right),
            top_y=footer_top_y,
            font=self.font,
            amount=total_gold,
            icon=gold_icon,
        )
        
        if left_clicked:
            player_eid, inp = self._get_player_input(world)
            # Click sobre botón cerrar dentro de la barra de tabs
            if tabs_close_rect and tabs_close_rect.collidepoint(mouse_pos):
                self.visible = False
                self.logger.debug("Inventory closed via tabs close button")
                return
            # Click sobre footer (informativo) => consumir
            if footer_rect and footer_rect.collidepoint(mouse_pos):
                return
            # Click sobre pestañas
            if self._tab_rects and any(r.collidepoint(mouse_pos) for r in self._tab_rects):
                for i, r in enumerate(self._tab_rects):
                    if r.collidepoint(mouse_pos):
                        if i != self.active_tab_index:
                            self.active_tab_index = i
                        break
                return
            # Si el click cayó en el área reservada para tabs (pero no en ninguna pestaña/close), consumirlo para evitar
            # que se interprete como click en slots.
            if panel_rect.collidepoint(mouse_pos):
                if panel_rect.y <= mouse_pos[1] <= panel_rect.y + used_h:
                    return
            if inp:
                for idx, stack in enumerate(slots_to_draw):
                    if not stack:
                        continue
                    slot_rect = compute_slot_rect(grid_panel_rect, idx)
                    if slot_rect.collidepoint(mouse_pos):
                        # Detección de doble clic en mismo slot
                        last_idx = getattr(self, 'last_click_slot_idx', None)
                        last_time = getattr(self, 'last_click_time', 0)
                        if last_idx == idx and now - last_time <= getattr(self, 'double_click_threshold', 500):
                            logger.debug(f"[DEBUG][InventoryUI] double click on slot {idx} item {stack.item_id}")
                            inp.use_item = stack.item_id
                            logger.debug(f"[DEBUG][InventoryUI] use_item set to {stack.item_id}")
                            # Resetear estado doble clic
                            self.last_click_slot_idx = None
                            self.last_click_time = 0
                        else:
                            logger.debug(f"[DEBUG][InventoryUI] first click on slot {idx} item {stack.item_id}")
                            self.last_click_slot_idx = idx
                            self.last_click_time = now
                        break

