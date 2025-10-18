import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_editors.buildings.buildings_editor_config import (
    ICON_BACK, THUMB_SIZE, THUMB_PADDING, NAV_HEIGHT
)

class BuildingPickerEventHandler:
    def __init__(self, editor_state, controller, buildings):
        self.editor = editor_state
        self.ctrl = controller
        self.buildings = buildings
        self.back_icon = load_image(ICON_BACK, (NAV_HEIGHT, NAV_HEIGHT))
        # Track whether the last LMB-down produced a selection to prevent double fire
        self._last_lmb_down_consumed: bool = False

    def handle(self, ev, camera):
        # 1) ESC: si estoy arrastrando, cancelo drag; si no, cierro picker
        if ev.type == pygame.KEYDOWN and ev.key == pygame.K_ESCAPE:
            if self.editor.dragging_building:
                self.ctrl.stop_drag()
            else:
                self.ctrl.close_picker()  # implementa este método en tu picker controller
            return

        # 1.b) Scroll con rueda del mouse sobre el panel (si hay más de 3 filas)
        if ev.type == pygame.MOUSEWHEEL:
            panel_rect = getattr(self.editor, 'picker_panel_rect', None)
            if not panel_rect:
                return
            mx, my = pygame.mouse.get_pos()
            if not panel_rect.collidepoint(mx, my):
                return
            m = getattr(self.editor, 'picker_internal_margin', 8)
            pad = getattr(self.editor, 'picker_padding', 8)
            cw = getattr(self.editor, 'picker_cell_w', 64)
            ch = getattr(self.editor, 'picker_cell_h', 64)
            footer_h = getattr(self.editor, 'picker_footer_h', 0)
            max_cols = getattr(self.editor, 'picker_max_columns', None)
            needs_scroll = bool(getattr(self.editor, 'picker_needs_scroll', False))
            sb_pad = 4
            sb_w = int(getattr(self.editor, 'picker_scrollbar_w', 10) or 10) if needs_scroll else 0
            gx = panel_rect.left + m
            gy = panel_rect.top + m
            gw = max(0, panel_rect.w - 2 * m)
            gh = max(0, panel_rect.h - 2 * m - footer_h)
            gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
            cols = max(1, (gw_effective + pad) // (cw + pad))
            if max_cols:
                cols = min(cols, max_cols)
            has_back = bool(getattr(self.editor, 'history', []))
            total = len(getattr(self.editor, 'entries', [])) + (1 if has_back else 0)
            total_rows = max(1, (total + cols - 1) // cols)
            visible_rows = int(getattr(self.editor, 'picker_visible_rows', 3) or 3)
            max_scroll = max(0, total_rows - visible_rows)
            cur = int(getattr(self.editor, 'picker_scroll_row', 0) or 0)
            # pygame wheel: y > 0 up, y < 0 down
            new_val = cur - int(ev.y)
            new_val = max(0, min(max_scroll, new_val))
            self.editor.picker_scroll_row = new_val
            return

        # 1.c) Drag del scrollbar
        if ev.type == pygame.MOUSEMOTION and getattr(self.editor, 'picker_scroll_dragging', False):
            track_rect = getattr(self.editor, 'picker_scroll_track_rect', None)
            thumb_rect = getattr(self.editor, 'picker_scroll_thumb_rect', None)
            if not track_rect or not thumb_rect:
                return
            my = ev.pos[1]
            offset = int(getattr(self.editor, 'picker_scroll_drag_offset', 0) or 0)
            track_y = track_rect.top
            track_h = track_rect.height
            thumb_h = thumb_rect.height
            max_thumb_y = max(1, track_h - thumb_h)
            # Calcular fila a partir de posición del thumb
            new_thumb_top = max(0, min(max_thumb_y, (my - track_y - offset)))
            total_rows = max(1, int(getattr(self.editor, 'picker_rows_needed', 1)))
            visible_rows = max(1, int(getattr(self.editor, 'picker_visible_rows', 3)))
            denom = max(1, total_rows - visible_rows)
            frac = 0.0 if max_thumb_y == 0 else (new_thumb_top / max_thumb_y)
            new_scroll = int(round(frac * denom))
            self.editor.picker_scroll_row = max(0, min(denom, new_scroll))
            return

        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
            if getattr(self.editor, 'picker_scroll_dragging', False):
                self.editor.picker_scroll_dragging = False
                return

        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3:
            mx, my = ev.pos
            # Terminar drag del panel con RMB
            if getattr(self.editor, 'picker_dragging_panel', False):
                self.editor.picker_dragging_panel = False
                return
            # Drop de building con RMB
            if self.editor.dragging_building:
                self.ctrl.place_building((mx, my), camera, self.buildings)
                return

        if ev.type == pygame.MOUSEMOTION and getattr(self.editor, 'picker_dragging_panel', False):
            panel_rect = getattr(self.editor, 'picker_panel_rect', None)
            if not panel_rect:
                return
            mx, my = ev.pos
            offx, offy = getattr(self.editor, 'picker_drag_offset', (0, 0))
            new_left = int(mx - offx)
            new_top = int(my - offy)
            # Clamp a poco para no perder el panel por completo
            new_left = max(0, new_left)
            new_top = max(0, new_top)
            self.editor.picker_manual_pos = (new_left, new_top)
            return

        # 2) Clic derecho: iniciar drag de panel o de ítem
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            mx, my = ev.pos

            panel_rect = getattr(self.editor, 'picker_panel_rect', None)
            if not panel_rect:
                return

            # Métricas y layout
            m = getattr(self.editor, 'picker_internal_margin', 8)
            cw = getattr(self.editor, 'picker_cell_w', THUMB_SIZE)
            ch = getattr(self.editor, 'picker_cell_h', THUMB_SIZE)
            pad = getattr(self.editor, 'picker_padding', THUMB_PADDING)
            footer_h = getattr(self.editor, 'picker_footer_h', 0)
            max_cols = getattr(self.editor, 'picker_max_columns', None)
            scroll_row = int(getattr(self.editor, 'picker_scroll_row', 0) or 0)
            visible_rows = int(getattr(self.editor, 'picker_visible_rows', 3) or 3)
            needs_scroll = bool(getattr(self.editor, 'picker_needs_scroll', False))
            sb_pad = 4
            sb_w = int(getattr(self.editor, 'picker_scrollbar_w', 10) or 10) if needs_scroll else 0

            gx = panel_rect.left + m
            gy = panel_rect.top + m
            gw = max(0, panel_rect.w - 2 * m)
            gh = max(0, panel_rect.h - 2 * m - footer_h)

            # RMB fuera del panel: ignorar
            if not panel_rect.collidepoint(mx, my):
                return

            # RMB en scrollbar: ignorar
            track_rect = getattr(self.editor, 'picker_scroll_track_rect', None)
            if needs_scroll and track_rect and track_rect.collidepoint(mx, my):
                return

            # RMB fuera del grid: iniciar drag del panel si el clic fue dentro del panel (y no en scrollbar)
            if not pygame.Rect(gx, gy, gw, gh).collidepoint(mx, my):
                track_rect = getattr(self.editor, 'picker_scroll_track_rect', None)
                if panel_rect.collidepoint(mx, my) and not (needs_scroll and track_rect and track_rect.collidepoint(mx, my)):
                    self.editor.picker_dragging_panel = True
                    self.editor.picker_drag_offset = (mx - panel_rect.left, my - panel_rect.top)
                    if getattr(self.editor, 'picker_manual_pos', None) is None:
                        self.editor.picker_manual_pos = (panel_rect.left, panel_rect.top)
                return

            gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
            # Clic en zona reservada del scrollbar: ignorar
            if needs_scroll and mx >= gx + gw_effective:
                return

            cols = max(1, (gw_effective + pad) // (cw + pad))
            if max_cols:
                cols = min(cols, max_cols)
            col = (mx - gx) // (cw + pad)
            row = (my - gy) // (ch + pad)
            idx = int(row) * int(cols) + int(col)

            has_back = bool(getattr(self.editor, 'history', []))
            total = len(getattr(self.editor, 'entries', [])) + (1 if has_back else 0)
            vidx = scroll_row * int(cols) + idx
            if vidx < 0 or vidx >= total:
                return

            # Back como primer celda
            if has_back and vidx == 0:
                self.ctrl.go_back()
                return

            base = 1 if has_back else 0
            entry_idx = vidx - base
            entries = getattr(self.editor, 'entries', [])
            if 0 <= entry_idx < len(entries):
                entry = entries[entry_idx]
                if entry.is_dir:
                    self.ctrl.change_dir(entry.path)
                else:
                    # Iniciar drag con RMB
                    self.ctrl.start_drag(entry)
            return

        # 3) Clic con botón izquierdo (hit-test sobre la grilla del panel)
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            self._last_lmb_down_consumed = False
            mx, my = ev.pos

            panel_rect = getattr(self.editor, 'picker_panel_rect', None)
            if not panel_rect:
                return  # si la vista aún no configuró el panel, ignorar

            # Métricas internas del panel (inyectadas por la vista)
            m = getattr(self.editor, 'picker_internal_margin', 8)
            cw = getattr(self.editor, 'picker_cell_w', THUMB_SIZE)
            ch = getattr(self.editor, 'picker_cell_h', THUMB_SIZE)
            pad = getattr(self.editor, 'picker_padding', THUMB_PADDING)
            footer_h = getattr(self.editor, 'picker_footer_h', 0)
            max_cols = getattr(self.editor, 'picker_max_columns', None)
            scroll_row = int(getattr(self.editor, 'picker_scroll_row', 0) or 0)
            visible_rows = int(getattr(self.editor, 'picker_visible_rows', 3) or 3)
            needs_scroll = bool(getattr(self.editor, 'picker_needs_scroll', False))
            sb_pad = 4
            sb_w = int(getattr(self.editor, 'picker_scrollbar_w', 10) or 10) if needs_scroll else 0

            gx = panel_rect.left + m
            gy = panel_rect.top + m
            gw = max(0, panel_rect.w - 2 * m)
            gh = max(0, panel_rect.h - 2 * m - footer_h)

            # Si clic en scrollbar: manejar page jump o comenzar drag
            track_rect = getattr(self.editor, 'picker_scroll_track_rect', None)
            thumb_rect = getattr(self.editor, 'picker_scroll_thumb_rect', None)
            if needs_scroll and track_rect and pygame.Rect(track_rect).collidepoint(mx, my):
                if thumb_rect and thumb_rect.collidepoint(mx, my):
                    # iniciar drag del thumb
                    self.editor.picker_scroll_dragging = True
                    self.editor.picker_scroll_drag_offset = my - thumb_rect.top
                else:
                    # salto proporcional a la posición clicada en el track
                    track_y = track_rect.top
                    track_h = track_rect.height
                    thumb_h = thumb_rect.height if thumb_rect else 0
                    max_thumb_y = max(1, track_h - thumb_h)
                    pos = max(0, min(track_h, my - track_y))
                    total_rows = max(1, int(getattr(self.editor, 'picker_rows_needed', 1)))
                    denom = max(1, total_rows - max(1, visible_rows))
                    frac = 0.0 if track_h == 0 else (pos / track_h)
                    new_scroll = int(round(frac * denom))
                    self.editor.picker_scroll_row = max(0, min(denom, new_scroll))
                return

            # Click fuera del área de grilla -> no arrastra panel con LMB (RMB es el drag del panel)
            if not pygame.Rect(gx, gy, gw, gh).collidepoint(mx, my):
                return

            gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
            # Si el clic cae en el área reservada para el scrollbar, no seleccionar ítems
            if needs_scroll and mx >= gx + gw_effective:
                return
            cols = max(1, (gw_effective + pad) // (cw + pad))
            if max_cols:
                cols = min(cols, max_cols)
            col = (mx - gx) // (cw + pad)
            row = (my - gy) // (ch + pad)
            idx = int(row) * int(cols) + int(col)

            has_back = bool(getattr(self.editor, 'history', []))
            total = len(getattr(self.editor, 'entries', [])) + (1 if has_back else 0)
            # Índice absoluto en la lista virtual (con scroll)
            vidx = scroll_row * int(cols) + idx
            if vidx < 0 or vidx >= total:
                return

            # Primer icono = atrás
            if has_back and vidx == 0:
                self.ctrl.go_back()
                # Marcar el DOWN como consumido para que el UP no seleccione
                # accidentalmente una carpeta en el nuevo directorio.
                self._last_lmb_down_consumed = True
                return

            # Mapear índice visual a índice de entries
            base = 1 if has_back else 0
            entry_idx = vidx - base
            entries = getattr(self.editor, 'entries', [])
            if 0 <= entry_idx < len(entries):
                entry = entries[entry_idx]
                if entry.is_dir:
                    self.ctrl.change_dir(entry.path)
                    self._last_lmb_down_consumed = True
                else:
                    # Selección con LMB sin iniciar drag (drag es con RMB)
                    try:
                        self.editor.selected_entry = entry
                    except Exception:
                        pass
                    self._last_lmb_down_consumed = True
            return

        # 3.b) Fallback: si por solapamiento el DOWN no fue procesado, permitir selección en UP
        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
            try:
                # Si el DOWN ya se procesó, no repetir acción
                if self._last_lmb_down_consumed:
                    self._last_lmb_down_consumed = False
                    return
            except Exception:
                self._last_lmb_down_consumed = False
            mx, my = ev.pos

            panel_rect = getattr(self.editor, 'picker_panel_rect', None)
            if not panel_rect:
                return

            # Métricas internas del panel (inyectadas por la vista)
            m = getattr(self.editor, 'picker_internal_margin', 8)
            cw = getattr(self.editor, 'picker_cell_w', THUMB_SIZE)
            ch = getattr(self.editor, 'picker_cell_h', THUMB_SIZE)
            pad = getattr(self.editor, 'picker_padding', THUMB_PADDING)
            footer_h = getattr(self.editor, 'picker_footer_h', 0)
            max_cols = getattr(self.editor, 'picker_max_columns', None)
            scroll_row = int(getattr(self.editor, 'picker_scroll_row', 0) or 0)
            visible_rows = int(getattr(self.editor, 'picker_visible_rows', 3) or 3)
            needs_scroll = bool(getattr(self.editor, 'picker_needs_scroll', False))
            sb_pad = 4
            sb_w = int(getattr(self.editor, 'picker_scrollbar_w', 10) or 10) if needs_scroll else 0

            gx = panel_rect.left + m
            gy = panel_rect.top + m
            gw = max(0, panel_rect.w - 2 * m)
            gh = max(0, panel_rect.h - 2 * m - footer_h)

            # Si UP fuera del grid, no hacer nada
            if not pygame.Rect(gx, gy, gw, gh).collidepoint(mx, my):
                return
            gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
            if needs_scroll and mx >= gx + gw_effective:
                return
            cols = max(1, (gw_effective + pad) // (cw + pad))
            if max_cols:
                cols = min(cols, max_cols)
            col = (mx - gx) // (cw + pad)
            row = (my - gy) // (ch + pad)
            idx = int(row) * int(cols) + int(col)

            has_back = bool(getattr(self.editor, 'history', []))
            total = len(getattr(self.editor, 'entries', [])) + (1 if has_back else 0)
            vidx = scroll_row * int(cols) + int(idx)
            if vidx < 0 or vidx >= total:
                return
            if has_back and vidx == 0:
                self.ctrl.go_back()
                return
            base = 1 if has_back else 0
            entry_idx = vidx - base
            entries = getattr(self.editor, 'entries', [])
            if 0 <= entry_idx < len(entries):
                entry = entries[entry_idx]
                if entry.is_dir:
                    self.ctrl.change_dir(entry.path)
                else:
                    try:
                        self.editor.selected_entry = entry
                    except Exception:
                        pass
            # Reset flag after UP handling
            self._last_lmb_down_consumed = False
            return