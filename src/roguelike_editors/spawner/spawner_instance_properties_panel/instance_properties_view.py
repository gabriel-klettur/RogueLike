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
                except (TypeError, ValueError):
                    value = repr(v)
                items.append((key, value))
        return items

    def render(self, controller, screen: pygame.Surface, *, anchor=(420, 120)):
        model = controller.model
        if not getattr(model, 'visible', False):
            self.panel_rect = None
            self.content_height = 0
            return None
        # Anchor to the right edge of the screen: ignore anchor.x, keep anchor.y
        _, y = anchor
        width = 440
        height = 360
        # Margin from right/top edges
        margin_right = 20
        x = max(0, int(screen.get_width() - width - margin_right))
        self.panel_rect = pygame.Rect(x, y, width, height)
        surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
        surf.fill((24, 24, 24, 230))
        pygame.draw.rect(surf, (100, 100, 100), surf.get_rect(), 2)
        try:
            title_font = pygame.font.SysFont(None, 22)
            font = pygame.font.SysFont(None, 18)
            header = "Spawner Instance Properties"
            title = title_font.render(header, True, (240, 240, 240))
            surf.blit(title, (10, 6))
            # Rows
            y_off = 30
            rows = controller.get_rows()
            row_h = 20
            padding_bottom = 6
            # Compute Visuals section height via visuals view (delegation)
            visuals_rows = controller.get_visuals_rows()
            visuals_spacing = 8  # spacing before visuals section
            # y where the visuals section begins
            visuals_total_h = visuals_spacing + row_h + row_h + (len(visuals_rows) if len(visuals_rows) > 0 else 1) * row_h
            # content height is rows height + visuals block + padding
            self.content_height = (len(rows) * row_h) + visuals_total_h + padding_bottom
            viewport_top = y_off
            viewport_bottom = height - 8
            # Clamp scroll to avoid empty panel when out of range (e.g., after refactors/state changes)
            viewport_h = max(0, viewport_bottom - viewport_top)
            max_scroll = max(0, int(self.content_height) - int(viewport_h))
            try:
                cur_scroll = int(getattr(model, 'scroll_offset', 0) or 0)
            except (TypeError, ValueError):
                cur_scroll = 0
            if cur_scroll < 0:
                cur_scroll = 0
            if cur_scroll > max_scroll:
                cur_scroll = max_scroll
            try:
                model.scroll_offset = cur_scroll
            except Exception:
                pass
            scroll = cur_scroll
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

            # Visuals section (delegated to visualsView)
            base_rows_h = len(rows) * row_h
            y_start_visuals = y_off + base_rows_h - scroll + visuals_spacing
            visuals_total_h = controller.visuals.view.render_table(
                surf,
                self.panel_rect,
                title_font=title_font,
                font=font,
                width=width,
                row_h=row_h,
                y_start_visuals=y_start_visuals,
                viewport_top=viewport_top,
                viewport_bottom=viewport_bottom,
            )
        except (AttributeError, TypeError, ValueError, pygame.error):
            pass
        screen.blit(surf, self.panel_rect.topleft)
        # UI blocker
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.panel_rect)
        except (ImportError, AttributeError, TypeError):
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
        except (AttributeError, TypeError, IndexError):
            pass
        # Hover tooltip for Visuals state names: show TitleCase ↔ snake_case equivalence
        try:
            mx, my = pygame.mouse.get_pos()
            # Visuals state tooltip uses rects from visualsModel
            vmodel = getattr(controller.visuals, 'model', None)
            if self.panel_rect and vmodel and getattr(vmodel, 'visuals_state_rects', None):
                local = (mx - self.panel_rect.left, my - self.panel_rect.top)
                for j, r in enumerate(vmodel.visuals_state_rects):
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
        except (AttributeError, TypeError, IndexError):
            pass
        # Hover tooltip for Visuals controls (folder/eye)
        try:
            mx, my = pygame.mouse.get_pos()
            if self.panel_rect:
                local = (mx - self.panel_rect.left, my - self.panel_rect.top)
                vmodel = getattr(controller.visuals, 'model', None)
                # Folder tooltip
                for j, r in enumerate(getattr(vmodel, 'visuals_browse_rects', []) or []):
                    try:
                        if r and r.collidepoint(local):
                            draw_tooltip(screen, mx, my, ["Abrir selector de templates (Buildings Picker)"])
                            raise StopIteration
                    except StopIteration:
                        break
                    except (AttributeError, TypeError):
                        continue
                # Eye tooltip (toggle)
                for j, r in enumerate(getattr(vmodel, 'visuals_eye_rects', []) or []):
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
                            except (AttributeError, TypeError, ValueError):
                                label = hide_label
                            draw_tooltip(screen, mx, my, [label])
                            raise StopIteration
                    except StopIteration:
                        break
                    except (AttributeError, TypeError):
                        continue
        except (AttributeError, TypeError, ValueError):
            pass
        # Ephemeral toast (bottom-right of the panel)
        try:
            msg = getattr(model, 'toast_message', None)
            until_ms = int(getattr(model, 'toast_until_ms', 0) or 0)
            now = 0
            try:
                now = pygame.time.get_ticks()
            except (AttributeError, pygame.error):
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
                except (pygame.error, ValueError, TypeError):
                    pass
                box.blit(txt, (pad_x, pad_y))
                screen.blit(box, (bx, by))
        except (AttributeError, TypeError, ValueError, pygame.error):
            pass
        return self.panel_rect
