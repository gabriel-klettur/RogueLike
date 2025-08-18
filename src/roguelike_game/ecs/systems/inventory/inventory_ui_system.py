import os

import pygame
import logging
import math
from roguelike_game.ecs.components.item_models import load_items

import logging
logger = logging.getLogger(__name__)

class InventoryUISystem:
    """
    Sistema de UI para mostrar el inventario del jugador en pantalla.
    """
    # Constantes de estilo y layout
    BGCOLOR = (50, 50, 50)
    BORDER_COLOR = (200, 200, 200)
    CLOSE_BUTTON_COLOR = (200, 50, 50)
    SLOT_BG_COLOR = (80, 80, 80)
    SLOT_BORDER_COLOR = (150, 150, 150)
    TEXT_COLOR = (255, 255, 255)
    GRID_COLS = 5
    GRID_ROWS = 5
    PADDING = 10
    SLOT_SIZE = 64
    CLOSE_BUTTON_SIZE = 20
    # Visual de agarre (hold-to-grab)
    GRAB_PROGRESS_COLOR = (255, 255, 0)  # amarillo intenso
    GRAB_PROGRESS_ALPHA = 220
    # Borde pulsante sincronizado con el progreso
    PULSE_BORDER_COLOR = (255, 215, 0)
    PULSE_BASE_ALPHA = 90
    PULSE_MAX_ALPHA = 200
    PULSE_BASE_THICKNESS = 2
    PULSE_MAX_THICKNESS = 5
    PULSE_FREQ = 2.0  # pulsos por segundo
    # Colores de éxito (al finalizar la carga)
    GRAB_SUCCESS_COLOR = (80, 220, 120)  # verde
    PULSE_SUCCESS_COLOR = (80, 220, 120)
    # Umbral para considerar "listo para arrastrar" (mostrar verde antes de activar el drag)
    DRAG_READY_RATIO = 1.0

    def __init__(self, perf_log=None, items_path=None):
        """
        Inicializa InventoryUISystem, carga modelos de ítems y prepara fuentes e íconos.
        """
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items = load_items(items_path)
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
        # Pre-cargar superficies de íconos
        self.icon_surfaces = {}
        for item_id, model in self.items.items():
            icon = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
            if isinstance(icon, list):
                icon = icon[0]
            if icon:
                path = os.path.join(os.getcwd(), icon)
                try:
                    surf = pygame.image.load(path).convert_alpha()
                except Exception:
                    surf = None
                self.icon_surfaces[item_id] = surf

    def _ease_out_cubic(self, x: float) -> float:
        """Suavizado ease-out para el progreso visual (0..1)."""
        x = max(0.0, min(1.0, float(x)))
        return 1.0 - pow(1.0 - x, 3)

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

    def _compute_panel_rect(self, screen, num_slots):
        """Calcula y retorna el Rect del panel basado en número de slots y offset de drag."""
        cols = self.GRID_COLS
        rows = self.GRID_ROWS
        padding = self.PADDING
        size = self.SLOT_SIZE
        panel_w = cols * size + (cols + 1) * padding
        panel_h = rows * size + (rows + 1) * padding
        screen_w, screen_h = screen.get_size()
        center_x = (screen_w - panel_w) // 2
        center_y = (screen_h - panel_h) // 2
        x = center_x + self.drag_offset_x
        y = center_y + self.drag_offset_y
        return pygame.Rect(x, y, panel_w, panel_h)

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

    def _draw_panel(self, screen, panel_rect):
        """Dibuja background, borde y botón de cierre, maneja click de cierre."""
        pygame.draw.rect(screen, self.BGCOLOR, panel_rect)
        pygame.draw.rect(screen, self.BORDER_COLOR, panel_rect, 2)
        size = self.CLOSE_BUTTON_SIZE
        padding = self.PADDING
        x = panel_rect.x + panel_rect.width - size - padding
        y = panel_rect.y + padding
        close_rect = pygame.Rect(x, y, size, size)
        pygame.draw.rect(screen, self.CLOSE_BUTTON_COLOR, close_rect)
        text_surf = self.font.render("X", True, self.TEXT_COLOR)
        text_rect = text_surf.get_rect(center=close_rect.center)
        screen.blit(text_surf, text_rect)
        if pygame.mouse.get_pressed()[0] and close_rect.collidepoint(pygame.mouse.get_pos()):
            self.visible = False
            self.logger.debug("Inventory closed via close button")

    def _draw_slots(self, screen, panel_rect, slots, highlight_idx=None, grab_progress=0.0):
        """Dibuja los slots dentro del panel."""
        cols = self.GRID_COLS
        padding = self.PADDING
        size = self.SLOT_SIZE
        rows = self.GRID_ROWS
        total_slots = cols * rows
        for idx in range(total_slots):
            stack = slots[idx] if idx < len(slots) else None
            col = idx % cols
            row = idx // cols
            x = panel_rect.x + padding + col * (size + padding)
            y = panel_rect.y + padding + row * (size + padding)
            slot_rect = pygame.Rect(x, y, size, size)
            pygame.draw.rect(screen, self.SLOT_BG_COLOR, slot_rect)
            pygame.draw.rect(screen, self.SLOT_BORDER_COLOR, slot_rect, 1)
            if stack:
                surf = self.icon_surfaces.get(stack.item_id)
                if surf:
                    img = pygame.transform.scale(surf, (size - 10, size - 10))
                    screen.blit(img, (x + 5, y + 5))
                qty_surf = self.font.render(str(stack.quantity), True, self.TEXT_COLOR)
                qty_rect = qty_surf.get_rect(bottomright=(x + size - 5, y + size - 5))
                screen.blit(qty_surf, qty_rect)
            # Dibujar progreso de agarre (hold-to-drag) si aplica para este slot
            if highlight_idx is not None and idx == highlight_idx and grab_progress > 0.0:
                p = max(0.0, min(1.0, float(grab_progress)))
                pe = self._ease_out_cubic(p)
                # Superficie semitransparente
                overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                overlay.fill((0, 0, 0, 0))
                # Llenado progresivo de abajo hacia arriba (amarillo -> verde al completar)
                fill_h = int(size * pe)
                fill_rect = pygame.Rect(0, size - fill_h, size, fill_h)
                done = p >= self.DRAG_READY_RATIO
                base_color = self.GRAB_SUCCESS_COLOR if done else self.GRAB_PROGRESS_COLOR
                color = (*base_color, self.GRAB_PROGRESS_ALPHA)
                pygame.draw.rect(overlay, color, fill_rect)
                screen.blit(overlay, (x, y))
                # Borde pulsante sincronizado con el progreso (más intenso hacia el final)
                t = pygame.time.get_ticks() / 1000.0
                s = (math.sin(2.0 * math.pi * self.PULSE_FREQ * t) + 1.0) * 0.5
                pulse_factor = s * pe
                alpha = int(self.PULSE_BASE_ALPHA + (self.PULSE_MAX_ALPHA - self.PULSE_BASE_ALPHA) * pulse_factor)
                thickness = int(self.PULSE_BASE_THICKNESS + (self.PULSE_MAX_THICKNESS - self.PULSE_BASE_THICKNESS) * pulse_factor)
                border_overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                pulse_color = self.PULSE_SUCCESS_COLOR if done else self.PULSE_BORDER_COLOR
                pygame.draw.rect(border_overlay, (*pulse_color, alpha), border_overlay.get_rect(), max(1, thickness))
                screen.blit(border_overlay, (x, y))

    def update(self, world, screen, camera):
        """
        Update de UI de inventario: toggle, arrastre y render.
        """
        if not self._handle_toggle(world):
            return
        slots = self._get_slots(world)
        if not slots:
            return
        initial_rect = self._compute_panel_rect(screen, len(slots))
        self._handle_drag(initial_rect)
        panel_rect = self._compute_panel_rect(screen, len(slots))
        self.panel_rect = panel_rect
        self._draw_panel(screen, panel_rect)
        # Draw slots, ocultando el slot si ya está en arrastre y mostrando progreso si está en pre-agarre
        drag_sys = next((s for s in getattr(world, 'update_systems', []) if hasattr(s, 'dragging_idx')), None)
        drag_idx = getattr(drag_sys, 'dragging_idx', None) if drag_sys else None
        slots_to_draw = list(slots)
        if drag_idx is not None and 0 <= drag_idx < len(slots_to_draw):
            slots_to_draw[drag_idx] = None
        # Calcular progreso de agarre (hold) cuando aún no hay drag confirmado
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
        self._draw_slots(screen, panel_rect, slots_to_draw, highlight_idx, grab_progress)
        # Draw dragged item icon above panel
        if drag_idx is not None:
            stack = slots[drag_idx] if drag_idx < len(slots) else None
            if stack:
                surf = self.icon_surfaces.get(stack.item_id)
                if surf:
                    size = self.SLOT_SIZE - 10
                    img = pygame.transform.scale(surf, (size, size))
                    ghost = img.copy()
                    ghost.set_alpha(150)
                    mx, my = pygame.mouse.get_pos()
                    screen.blit(ghost, (mx - size//2, my - size//2))
                # Manejar uso de consumibles (doble clic izquierdo en slot)
                # Highlight del slot destino mientras se arrastra dentro del inventario
                mx2, my2 = pygame.mouse.get_pos()
                if panel_rect and panel_rect.collidepoint(mx2, my2):
                    rel_x = mx2 - panel_rect.x - self.PADDING
                    rel_y = my2 - panel_rect.y - self.PADDING
                    if rel_x >= 0 and rel_y >= 0:
                        col = int(rel_x // (self.SLOT_SIZE + self.PADDING))
                        row = int(rel_y // (self.SLOT_SIZE + self.PADDING))
                        dst_idx = row * self.GRID_COLS + col
                        sx = panel_rect.x + self.PADDING + col * (self.SLOT_SIZE + self.PADDING)
                        sy = panel_rect.y + self.PADDING + row * (self.SLOT_SIZE + self.PADDING)
                        slot_rect = pygame.Rect(sx, sy, self.SLOT_SIZE, self.SLOT_SIZE)
                        if 0 <= dst_idx < len(slots) and slot_rect.collidepoint(mx2, my2) and dst_idx != drag_idx:
                            overlay = pygame.Surface((self.SLOT_SIZE, self.SLOT_SIZE), pygame.SRCALPHA)
                            # Usamos el color de éxito (verde) semitransparente
                            pygame.draw.rect(overlay, (*self.GRAB_SUCCESS_COLOR, 80), overlay.get_rect())
                            screen.blit(overlay, (sx, sy))
                            # Borde para mayor claridad
                            border_overlay = pygame.Surface((self.SLOT_SIZE, self.SLOT_SIZE), pygame.SRCALPHA)
                            pygame.draw.rect(border_overlay, (*self.PULSE_SUCCESS_COLOR, 200), border_overlay.get_rect(), 3)
                            screen.blit(border_overlay, (sx, sy))
        # Map->Inventory drag feedback: overlay on hovered slot + ghost sprite
        drop_sys = next((s for s in getattr(world, 'update_systems', []) if hasattr(s, 'dragging_eid')), None)
        drop_eid = getattr(drop_sys, 'dragging_eid', None) if drop_sys else None
        if drop_eid is not None:
            # 1) Slot overlay with progressive fill if hovering panel
            try:
                hover_idx = getattr(drop_sys, 'hover_slot_idx', None)
                hover_start = getattr(drop_sys, 'hover_start_time', None)
                hover_threshold = getattr(drop_sys, 'hover_fill_threshold', 300)
                if hover_idx is not None and hover_start is not None and panel_rect:
                    cols = self.GRID_COLS
                    padding = self.PADDING
                    size = self.SLOT_SIZE
                    col = int(hover_idx % cols)
                    row = int(hover_idx // cols)
                    x = panel_rect.x + padding + col * (size + padding)
                    y = panel_rect.y + padding + row * (size + padding)
                    # Progreso con easing y pulso
                    now_ts = pygame.time.get_ticks()
                    p = max(0.0, min(1.0, (now_ts - hover_start) / max(1, hover_threshold)))
                    pe = self._ease_out_cubic(p)
                    # Relleno (amarillo -> verde al completar)
                    overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                    overlay.fill((0, 0, 0, 0))
                    fill_h = int(size * pe)
                    fill_rect = pygame.Rect(0, size - fill_h, size, fill_h)
                    done = p >= 0.999
                    base_color = self.GRAB_SUCCESS_COLOR if done else self.GRAB_PROGRESS_COLOR
                    color = (*base_color, self.GRAB_PROGRESS_ALPHA)
                    pygame.draw.rect(overlay, color, fill_rect)
                    screen.blit(overlay, (x, y))
                    # Borde pulsante
                    t = now_ts / 1000.0
                    s = (math.sin(2.0 * math.pi * self.PULSE_FREQ * t) + 1.0) * 0.5
                    pulse_factor = s * pe
                    alpha = int(self.PULSE_BASE_ALPHA + (self.PULSE_MAX_ALPHA - self.PULSE_BASE_ALPHA) * pulse_factor)
                    thickness = int(self.PULSE_BASE_THICKNESS + (self.PULSE_MAX_THICKNESS - self.PULSE_BASE_THICKNESS) * pulse_factor)
                    border_overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                    pulse_color = self.PULSE_SUCCESS_COLOR if done else self.PULSE_BORDER_COLOR
                    pygame.draw.rect(border_overlay, (*pulse_color, alpha), border_overlay.get_rect(), max(1, thickness))
                    screen.blit(border_overlay, (x, y))
            except Exception:
                pass
            # 2) Ghost del item siendo arrastrado
            comps2 = world.components
            sprite_comp = comps2.get('Sprite', {}).get(drop_eid)
            if sprite_comp:
                img2 = sprite_comp.image
                scale_comp2 = comps2.get('Scale', {}).get(drop_eid)
                scale_factor2 = camera.zoom * (scale_comp2.scale if scale_comp2 else 1.0)
                spr2 = pygame.transform.rotozoom(img2, 0, scale_factor2)
                ghost2 = spr2.copy()
                ghost2.set_alpha(150)
                mx2, my2 = pygame.mouse.get_pos()
                rect2 = ghost2.get_rect(center=(mx2, my2))
                screen.blit(ghost2, rect2)
        now = pygame.time.get_ticks()
        left_pressed = pygame.mouse.get_pressed()[0]
        mouse_pos = pygame.mouse.get_pos()
        left_clicked = left_pressed and not self.prev_left_pressed
        self.prev_left_pressed = left_pressed
        
        if left_clicked:
            player_eid, inp = self._get_player_input(world)
            if inp:
                for idx, stack in enumerate(slots):
                    if not stack:
                        continue
                    col = idx % self.GRID_COLS
                    row = idx // self.GRID_COLS
                    x = panel_rect.x + self.PADDING + col * (self.SLOT_SIZE + self.PADDING)
                    y = panel_rect.y + self.PADDING + row * (self.SLOT_SIZE + self.PADDING)
                    slot_rect = pygame.Rect(x, y, self.SLOT_SIZE, self.SLOT_SIZE)
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

