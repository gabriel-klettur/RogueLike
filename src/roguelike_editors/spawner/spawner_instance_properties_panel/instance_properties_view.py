from __future__ import annotations

from typing import Any, Dict, List, Tuple

import pygame
from roguelike_ui.ui_helpers import draw_tooltip
from roguelike_ui.widgets.hover import draw_hover


class InstancePropertiesView:
    def __init__(self) -> None:
        self.panel_rect: pygame.Rect | None = None
        self.content_height: int = 0
        # Combobox rects (screen-local to panel surface coordinates)
        self.template_combo_rect: pygame.Rect | None = None
        self.template_list_rect: pygame.Rect | None = None
        # Visuals per-row rects (local to panel surface)
        self.visuals_template_rects: list[pygame.Rect] = []
        self.visuals_plus_rects: list[pygame.Rect] = []
        self.visuals_browse_rects: list[pygame.Rect] = []
        self.visuals_eye_rects: list[pygame.Rect] = []
        # Track first column (State) rects for hover tooltips
        self.visuals_state_rects: list[pygame.Rect] = []

    def _flatten(self, data: Dict[str, Any], prefix: str = "") -> List[Tuple[str, str]]:
        items: List[Tuple[str, str]] = []
        for k, v in (data or {}).items():
            key = f"{prefix}.{k}" if prefix else str(k)
            if isinstance(v, dict):
                items.extend(self._flatten(v, key))
            else:
                try:
                    if isinstance(v, (list, tuple)):
                        value = str(v)
                    else:
                        value = str(v)
                except Exception:
                    value = repr(v)
                items.append((key, value))
        return items

    def render(self, controller, screen: pygame.Surface, *, anchor=(420, 120)):
        model = controller.model
        if not getattr(model, 'visible', False):
            self.panel_rect = None
            self.content_height = 0
            return None
        x, y = anchor
        width = 440
        height = 360
        self.panel_rect = pygame.Rect(x, y, width, height)
        surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
        surf.fill((24, 24, 24, 230))
        pygame.draw.rect(surf, (100, 100, 100), surf.get_rect(), 2)
        try:
            title_font = pygame.font.SysFont(None, 22)
            font = pygame.font.SysFont(None, 18)
            header = "Instance Properties"
            title = title_font.render(header, True, (240, 240, 240))
            surf.blit(title, (10, 6))
            # Rows
            y_off = 30
            rows = controller.get_rows()
            row_h = 20
            padding_bottom = 6
            # Pre-compute Visuals section height for total content_height
            visuals_rows = controller.get_visuals_rows()
            visuals_title_h = row_h  # one line title
            visuals_header_h = row_h  # table header
            visuals_rows_h = (len(visuals_rows) if len(visuals_rows) > 0 else 1) * row_h
            visuals_spacing = 8  # spacing before visuals section
            visuals_total_h = visuals_spacing + visuals_title_h + visuals_header_h + visuals_rows_h
            self.content_height = (len(rows) * row_h) + visuals_total_h + padding_bottom
            viewport_top = y_off
            viewport_bottom = height - 8
            scroll = int(getattr(model, 'scroll_offset', 0) or 0)
            # Reset combo rects each frame
            self.template_combo_rect = None
            self.template_list_rect = None
            for i, (k, v) in enumerate(rows):
                row_y = y_off + i * row_h - scroll
                if row_y + row_h < viewport_top or row_y > viewport_bottom:
                    continue
                row_rect_local = pygame.Rect(6, row_y - 2, width - 12, row_h)
                if getattr(model, 'editing_row_index', None) == i:
                    draw_hover(surf, row_rect_local, color=(60, 100, 160, 120))
                elif getattr(model, 'hovered_index', None) == i:
                    draw_hover(surf, row_rect_local, color=(60, 60, 60, 80))
                key_text = font.render(str(k), True, (160, 200, 255))
                surf.blit(key_text, (10, row_y))
                if getattr(model, 'editing_row_index', None) == i and controller.is_editing():
                    ti = controller.get_text_input()
                    if ti is not None:
                        ti.draw(surf, 210, row_y, color=(255, 255, 255))
                else:
                    # Special rendering for template_id as combobox
                    if str(k) == 'template_id':
                        combo_w = width - 220
                        combo_h = row_h - 2
                        combo_rect = pygame.Rect(210, row_y - 1, combo_w, combo_h)
                        pygame.draw.rect(surf, (40, 40, 40), combo_rect)
                        pygame.draw.rect(surf, (120, 120, 120), combo_rect, 1)
                        # Arrow box
                        arrow_w = 16
                        arrow_rect = pygame.Rect(combo_rect.right - arrow_w - 2, combo_rect.y + 1, arrow_w, combo_rect.height - 2)
                        pygame.draw.rect(surf, (60, 60, 60), arrow_rect)
                        pygame.draw.polygon(surf, (220, 220, 220), [
                            (arrow_rect.centerx - 4, arrow_rect.centery - 2),
                            (arrow_rect.centerx + 4, arrow_rect.centery - 2),
                            (arrow_rect.centerx, arrow_rect.centery + 3)
                        ])
                        # Current value text clipped to combo_rect
                        cur_txt = font.render(str(v), True, (230, 230, 230))
                        surf.blit(cur_txt, (combo_rect.x + 6, combo_rect.y + 2))
                        self.template_combo_rect = combo_rect
                        # Dropdown list if open
                        if getattr(model, 'template_combo_open', False):
                            options = controller.get_template_options()
                            visible_rows = min(8, max(1, len(options)))
                            list_h = visible_rows * row_h
                            list_rect = pygame.Rect(combo_rect.x, combo_rect.bottom + 2, combo_rect.width, list_h)
                            # Background
                            pygame.draw.rect(surf, (32, 32, 32), list_rect)
                            pygame.draw.rect(surf, (120, 120, 120), list_rect, 1)
                            start = int(getattr(model, 'template_scroll_offset', 0) or 0)
                            end = min(len(options), start + visible_rows)
                            cur_idx = controller.get_current_template_index()
                            for j, opt in enumerate(options[start:end]):
                                oy = list_rect.y + j * row_h
                                item_rect = pygame.Rect(list_rect.x + 2, oy, list_rect.width - 4, row_h)
                                abs_idx = start + j
                                # Hover highlight
                                if getattr(model, 'template_hovered_index', None) == abs_idx:
                                    draw_hover(surf, item_rect, color=(80, 80, 80, 120))
                                # Selected mark
                                if cur_idx is not None and cur_idx == abs_idx:
                                    pygame.draw.rect(surf, (70, 100, 160), item_rect, 1)
                                txt = font.render(str(opt), True, (230, 230, 230))
                                surf.blit(txt, (item_rect.x + 6, oy + 2))
                            self.template_list_rect = list_rect
                    else:
                        val_text = font.render(str(v), True, (230, 230, 230))
                        surf.blit(val_text, (210, row_y))

            # Visuals section (editable Template column)
            base_rows_h = len(rows) * row_h
            y_start_visuals = y_off + base_rows_h - scroll + visuals_spacing
            # Title
            if not (y_start_visuals + row_h < viewport_top or y_start_visuals > viewport_bottom):
                vis_title = title_font.render("Visuals", True, (240, 240, 180))
                surf.blit(vis_title, (10, y_start_visuals))
            # Table header
            y_header = y_start_visuals + row_h
            if not (y_header + row_h < viewport_top or y_header > viewport_bottom):
                # Columns: State | Instancia | Template
                col1_x, col2_x, col3_x = 10, 210, 310
                hdr1 = font.render("State", True, (180, 220, 255))
                hdr2 = font.render("Instancia", True, (180, 220, 255))
                hdr3 = font.render("Template", True, (180, 220, 255))
                surf.blit(hdr1, (col1_x, y_header))
                surf.blit(hdr2, (col2_x, y_header))
                surf.blit(hdr3, (col3_x, y_header))
                # underline
                pygame.draw.line(surf, (120, 120, 120), (8, y_header + row_h - 2), (width - 8, y_header + row_h - 2), 1)
            # Rows
            y_rows = y_header + row_h
            # Reset rects
            self.visuals_template_rects = []
            self.visuals_plus_rects = []
            self.visuals_browse_rects = []
            self.visuals_state_rects = []
            if len(visuals_rows) == 0:
                if not (y_rows + row_h < viewport_top or y_rows > viewport_bottom):
                    txt = font.render("(sin visuals)", True, (160, 160, 160))
                    surf.blit(txt, (10, y_rows))
            else:
                editing_state = getattr(model, 'visuals_editing_state', None)
                for j, (state, inst_id, tpl_id) in enumerate(visuals_rows):
                    ry = y_rows + j * row_h
                    if ry + row_h < viewport_top or ry > viewport_bottom:
                        # Keep rects length aligned
                        self.visuals_template_rects.append(pygame.Rect(0, 0, 0, 0))
                        self.visuals_plus_rects.append(pygame.Rect(0, 0, 0, 0))
                        self.visuals_browse_rects.append(pygame.Rect(0, 0, 0, 0))
                        self.visuals_eye_rects.append(pygame.Rect(0, 0, 0, 0))
                        self.visuals_state_rects.append(pygame.Rect(0, 0, 0, 0))
                        continue
                    col1_x, col2_x, col3_x = 10, 210, 310
                    t1 = font.render(str(state), True, (230, 230, 230))
                    t2 = font.render(str(inst_id), True, (230, 230, 230))
                    # Template cell rect and optional editing input + '+' button
                    template_rect = pygame.Rect(col3_x, ry - 1, width - col3_x - 10, row_h - 2)
                    # Right-side controls inside template cell: [eye][browse][+] con separaciones de 2px
                    control_h = template_rect.height - 4
                    plus_rect = pygame.Rect(template_rect.right - 18, template_rect.y + 2, 16, control_h)
                    browse_rect = pygame.Rect(plus_rect.left - 18, template_rect.y + 2, 16, control_h)
                    eye_rect = pygame.Rect(browse_rect.left - 18, template_rect.y + 2, 16, control_h)
                    self.visuals_template_rects.append(template_rect)
                    self.visuals_plus_rects.append(plus_rect)
                    self.visuals_browse_rects.append(browse_rect)
                    self.visuals_eye_rects.append(eye_rect)
                    # State label cell rect for hover detection
                    state_rect = pygame.Rect(col1_x, ry - 1, (col2_x - col1_x) - 4, row_h - 2)
                    self.visuals_state_rects.append(state_rect)
                    # Draw label cells
                    surf.blit(t1, (col1_x, ry))
                    surf.blit(t2, (col2_x, ry))
                    # If this row is being edited -> draw input and action buttons
                    if editing_state is not None and str(editing_state) == str(state) and controller.get_text_input() is not None and controller.get_text_input().active:
                        pygame.draw.rect(surf, (40, 40, 40), template_rect)
                        # Validation: check current input and paint border accordingly
                        ok, msg = controller.get_visual_input_validation(str(state))
                        border_col = (120, 120, 120) if ok else (200, 80, 80)
                        pygame.draw.rect(surf, border_col, template_rect, 1)
                        # Subtle overlay if this row is hidden in editor
                        try:
                            if not controller.is_visual_building_visible(str(state)):
                                draw_hover(surf, template_rect, color=(120, 40, 40, 70))
                        except Exception:
                            pass
                        # Draw text input inside the template_rect (padding)
                        ti = controller.get_text_input()
                        if ti is not None:
                            ti.draw(surf, template_rect.x + 4, template_rect.y + 2, color=(255, 255, 255))
                        # '+' button
                        pygame.draw.rect(surf, (60, 60, 60), plus_rect)
                        pygame.draw.rect(surf, (150, 150, 150), plus_rect, 1)
                        cx, cy = plus_rect.centerx, plus_rect.centery
                        pygame.draw.line(surf, (120, 220, 120), (cx - 4, cy), (cx + 4, cy), 2)
                        pygame.draw.line(surf, (120, 220, 120), (cx, cy - 4), (cx, cy + 4), 2)
                        # 'browse' button (folder)
                        pygame.draw.rect(surf, (60, 60, 60), browse_rect)
                        pygame.draw.rect(surf, (150, 150, 150), browse_rect, 1)
                        bx, by = browse_rect.x + 3, browse_rect.y + 3
                        pygame.draw.rect(surf, (230, 200, 120), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 0)
                        pygame.draw.rect(surf, (160, 130, 60), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 1)
                        pygame.draw.rect(surf, (230, 200, 120), (bx + 2, by + 2, 8, 6), 0)
                        # 'eye' button (toggle visible en edición)
                        visible = True
                        try:
                            visible = bool(controller.is_visual_building_visible(str(state)))
                        except Exception:
                            visible = True
                        pygame.draw.rect(surf, (60, 60, 60), eye_rect)
                        pygame.draw.rect(surf, (150, 150, 150), eye_rect, 1)
                        ex, ey = eye_rect.centerx, eye_rect.centery
                        pygame.draw.ellipse(surf, (220, 220, 220), (eye_rect.x + 3, eye_rect.y + 4, eye_rect.w - 6, eye_rect.h - 8), 1)
                        pygame.draw.circle(surf, (220, 220, 220) if visible else (120,120,120), (ex, ey), 3)
                        if not visible:
                            pygame.draw.line(surf, (200, 80, 80), (eye_rect.left + 3, eye_rect.bottom - 3), (eye_rect.right - 3, eye_rect.top + 3), 2)
                    else:
                        # Render template id as text; N/A en ámbar, y atenuado si está oculto en edición
                        visible = True
                        try:
                            visible = bool(controller.is_visual_building_visible(str(state)))
                        except Exception:
                            visible = True
                        # Subtle overlay if hidden in editor
                        if not visible:
                            try:
                                draw_hover(surf, template_rect, color=(120, 40, 40, 70))
                            except Exception:
                                pass
                        base_c = (230, 230, 230) if str(tpl_id).upper() != 'N/A' else (220, 180, 120)
                        c3 = base_c if visible else (150, 150, 150)
                        t3 = font.render(str(tpl_id), True, c3)
                        surf.blit(t3, (col3_x, ry))
                        # Draw action buttons even when not editing: +, folder, eye
                        pygame.draw.rect(surf, (60, 60, 60), plus_rect)
                        pygame.draw.rect(surf, (150, 150, 150), plus_rect, 1)
                        cx, cy = plus_rect.centerx, plus_rect.centery
                        pygame.draw.line(surf, (120, 220, 120), (cx - 4, cy), (cx + 4, cy), 2)
                        pygame.draw.line(surf, (120, 220, 120), (cx, cy - 4), (cx, cy + 4), 2)
                        # Folder
                        pygame.draw.rect(surf, (60, 60, 60), browse_rect)
                        pygame.draw.rect(surf, (150, 150, 150), browse_rect, 1)
                        bx, by = browse_rect.x + 3, browse_rect.y + 3
                        pygame.draw.rect(surf, (230, 200, 120), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 0)
                        pygame.draw.rect(surf, (160, 130, 60), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 1)
                        pygame.draw.rect(surf, (230, 200, 120), (bx + 2, by + 2, 8, 6), 0)
                        # Eye
                        visible = True
                        try:
                            visible = bool(controller.is_visual_building_visible(str(state)))
                        except Exception:
                            visible = True
                        pygame.draw.rect(surf, (60, 60, 60), eye_rect)
                        pygame.draw.rect(surf, (150, 150, 150), eye_rect, 1)
                        ex, ey = eye_rect.centerx, eye_rect.centery
                        pygame.draw.ellipse(surf, (220, 220, 220), (eye_rect.x + 3, eye_rect.y + 4, eye_rect.w - 6, eye_rect.h - 8), 1)
                        pygame.draw.circle(surf, (220, 220, 220) if visible else (120,120,120), (ex, ey), 3)
                        if not visible:
                            pygame.draw.line(surf, (200, 80, 80), (eye_rect.left + 3, eye_rect.bottom - 3), (eye_rect.right - 3, eye_rect.top + 3), 2)
        except Exception:
            pass
        screen.blit(surf, self.panel_rect.topleft)
        # UI blocker
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.panel_rect)
        except Exception:
            pass
        # Hover tooltip shows key path
        try:
            hi = getattr(model, 'hovered_index', None)
            if hi is not None:
                rows = controller.get_rows()
                if 0 <= hi < len(rows):
                    key, _ = rows[hi]
                    mx, my = pygame.mouse.get_pos()
                    draw_tooltip(screen, mx, my, [key])
        except Exception:
            pass
        # Hover tooltip for Visuals state names: show TitleCase ↔ snake_case equivalence
        try:
            mx, my = pygame.mouse.get_pos()
            if self.panel_rect and self.visuals_state_rects:
                local = (mx - self.panel_rect.left, my - self.panel_rect.top)
                for j, r in enumerate(self.visuals_state_rects):
                    if r and r.collidepoint(local):
                        vis_rows = controller.get_visuals_rows()
                        if 0 <= j < len(vis_rows):
                            state = str(vis_rows[j][0])
                            # Build snake_case equivalent
                            s = state
                            snake = []
                            for i, ch in enumerate(s):
                                if ch.isupper() and i > 0:
                                    snake.append('_')
                                snake.append(ch.lower())
                            snake_str = ''.join(snake)
                            draw_tooltip(screen, mx, my, [f"{state} ↔ {snake_str}"])
                        break
        except Exception:
            pass
        # Hover tooltip for Visuals controls (folder/eye/plus)
        try:
            mx, my = pygame.mouse.get_pos()
            if self.panel_rect:
                local = (mx - self.panel_rect.left, my - self.panel_rect.top)
                # Plus tooltip
                for j, r in enumerate(self.visuals_plus_rects or []):
                    try:
                        if r and r.collidepoint(local):
                            draw_tooltip(screen, mx, my, ["Crear instancia de building para este estado"])
                            raise StopIteration
                    except StopIteration:
                        break
                    except Exception:
                        continue
                # Folder tooltip
                for j, r in enumerate(self.visuals_browse_rects or []):
                    try:
                        if r and r.collidepoint(local):
                            draw_tooltip(screen, mx, my, ["Abrir selector de templates (Buildings Picker)"])
                            raise StopIteration
                    except StopIteration:
                        break
                    except Exception:
                        continue
                # Eye tooltip (toggle)
                for j, r in enumerate(self.visuals_eye_rects or []):
                    try:
                        if r and r.collidepoint(local):
                            # Decide label based on current visibility
                            vis_rows = controller.get_visuals_rows()
                            state = str(vis_rows[j][0]) if 0 <= j < len(vis_rows) else None
                            show_label = "Mostrar en edición"
                            hide_label = "Ocultar en edición"
                            label = hide_label
                            try:
                                if state is not None and not controller.is_visual_building_visible(state):
                                    label = show_label
                            except Exception:
                                label = hide_label
                            draw_tooltip(screen, mx, my, [label])
                            raise StopIteration
                    except StopIteration:
                        break
                    except Exception:
                        continue
        except Exception:
            pass
        # Ephemeral toast (bottom-right of the panel)
        try:
            msg = getattr(model, 'toast_message', None)
            until_ms = int(getattr(model, 'toast_until_ms', 0) or 0)
            now = 0
            try:
                now = pygame.time.get_ticks()
            except Exception:
                now = 0
            if msg and now < until_ms and self.panel_rect is not None:
                toast_font = pygame.font.SysFont(None, 18)
                txt = toast_font.render(str(msg), True, (255, 255, 255))
                pad_x, pad_y = 10, 6
                box_w = txt.get_width() + pad_x * 2
                box_h = txt.get_height() + pad_y * 2
                bx = self.panel_rect.right - box_w - 12
                by = self.panel_rect.bottom - box_h - 12
                box = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
                box.fill((20, 20, 20, 210))
                pygame.draw.rect(box, (200, 200, 200), box.get_rect(), 1)
                # subtle shadow
                try:
                    shadow = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
                    shadow.fill((0, 0, 0, 100))
                    screen.blit(shadow, (bx + 2, by + 2))
                except Exception:
                    pass
                box.blit(txt, (pad_x, pad_y))
                screen.blit(box, (bx, by))
        except Exception:
            pass
        return self.panel_rect
