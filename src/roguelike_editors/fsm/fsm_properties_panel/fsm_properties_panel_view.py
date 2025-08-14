from __future__ import annotations


class FsmPropertiesPanelView:
    def __init__(self) -> None:
        # Geometry caches filled at render
        self.panel_rect = None
        self.header_rect = None
        self.tabs_nodes_rect = None
        self.tabs_trans_rect = None
        self.set_prev_rect = None
        self.set_next_rect = None
        self.set_combo_rect = None
        self.item_prev_rect = None
        self.item_next_rect = None
        self.item_combo_rect = None
        self.value_col_x = 200

    def render(self, model, screen, *, anchor=(20, 120)):
        if not getattr(model, 'visible', False):
            return None
        try:
            import pygame  # type: ignore
            x, y = anchor
            w, h = 540, 420
            self.panel_rect = pygame.Rect(x, y, w, h)
            surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            surf.fill((20, 20, 20, 220))
            # Yellow border
            pygame.draw.rect(surf, (240, 210, 60), surf.get_rect(), 2)
            try:
                title_font = pygame.font.SysFont(None, 22)
                font = pygame.font.SysFont(None, 18)
                header_h = 30
                tabs_h = 24
                row_h = 22
                # Header
                self.header_rect = pygame.Rect(0, 0, w, header_h)
                pygame.draw.rect(surf, (28, 28, 28), self.header_rect)
                label = title_font.render("FSM Properties", True, (240, 240, 240))
                surf.blit(label, (10, 6))
                # Tabs bar
                tabs_y = header_h
                pygame.draw.rect(surf, (34, 34, 34), pygame.Rect(0, tabs_y, w, tabs_h))
                # Nodes tab
                self.tabs_nodes_rect = pygame.Rect(8, tabs_y + 2, 100, tabs_h - 4)
                self.tabs_trans_rect = pygame.Rect(8 + 100 + 8, tabs_y + 2, 120, tabs_h - 4)
                def _tab(rect, text, active):
                    bg = (70, 90, 130) if active else (60, 60, 60)
                    fg = (255, 255, 255) if active else (220, 220, 220)
                    pygame.draw.rect(surf, bg, rect, border_radius=3)
                    pygame.draw.rect(surf, (80, 80, 80), rect, 1, border_radius=3)
                    ts = font.render(text, True, fg)
                    surf.blit(ts, (rect.x + (rect.w - ts.get_width()) // 2, rect.y + (rect.h - ts.get_height()) // 2))
                _tab(self.tabs_nodes_rect, "Nodes", getattr(model, 'active_tab', 'nodes') == 'nodes')
                _tab(self.tabs_trans_rect, "Transitions", getattr(model, 'active_tab', 'nodes') == 'transitions')
                # Selectors row (Set and Node/Transition)
                sel_y = tabs_y + tabs_h + 2
                # Set selector
                tx = 10
                set_label = font.render("Set:", True, (230, 230, 230))
                surf.blit(set_label, (tx, sel_y + 3))
                cb_w = 200
                self.set_prev_rect = pygame.Rect(tx + 40, sel_y + 2, 20, 18)
                self.set_next_rect = pygame.Rect(tx + 40 + 20 + cb_w + 4, sel_y + 2, 20, 18)
                self.set_combo_rect = pygame.Rect(self.set_prev_rect.right + 4, sel_y + 2, cb_w, 18)
                pygame.draw.rect(surf, (70, 70, 70), self.set_prev_rect, 1)
                pygame.draw.rect(surf, (70, 70, 70), self.set_next_rect, 1)
                # arrows
                def _tri_left(r):
                    pygame.draw.polygon(surf, (200, 200, 200), [(r.x + 14, r.y + 3), (r.x + 6, r.y + 9), (r.x + 14, r.y + 15)])
                def _tri_right(r):
                    pygame.draw.polygon(surf, (200, 200, 200), [(r.x + 6, r.y + 3), (r.x + 14, r.y + 9), (r.x + 6, r.y + 15)])
                _tri_left(self.set_prev_rect)
                _tri_right(self.set_next_rect)
                pygame.draw.rect(surf, (200, 200, 200), self.set_combo_rect)
                pygame.draw.rect(surf, (255, 255, 255), self.set_combo_rect, 2)
                sid = getattr(model, 'selected_set_id', '') or ''
                ts = font.render(str(sid), True, (0, 0, 0))
                surf.blit(ts, (self.set_combo_rect.x + 6, self.set_combo_rect.y + 1))
                # Item selector
                item_y = sel_y + 22
                label_txt = 'Node:' if getattr(model, 'active_tab', 'nodes') == 'nodes' else 'Transition:'
                item_label = font.render(label_txt, True, (230, 230, 230))
                surf.blit(item_label, (tx, item_y + 3))
                self.item_prev_rect = pygame.Rect(tx + 80, item_y + 2, 20, 18)
                self.item_next_rect = pygame.Rect(tx + 80 + 20 + cb_w + 4, item_y + 2, 20, 18)
                self.item_combo_rect = pygame.Rect(self.item_prev_rect.right + 4, item_y + 2, cb_w, 18)
                pygame.draw.rect(surf, (70, 70, 70), self.item_prev_rect, 1)
                pygame.draw.rect(surf, (70, 70, 70), self.item_next_rect, 1)
                _tri_left(self.item_prev_rect)
                _tri_right(self.item_next_rect)
                pygame.draw.rect(surf, (200, 200, 200), self.item_combo_rect)
                pygame.draw.rect(surf, (255, 255, 255), self.item_combo_rect, 2)
                # Selected item text
                if getattr(model, 'active_tab', 'nodes') == 'nodes':
                    it_txt = getattr(model, 'selected_node_id', '') or ''
                else:
                    idx = getattr(model, 'selected_transition_index', None)
                    labels = getattr(model, 'transition_labels', []) or []
                    it_txt = labels[int(idx)] if (idx is not None and 0 <= int(idx) < len(labels)) else ''
                ts2 = font.render(str(it_txt), True, (0, 0, 0))
                surf.blit(ts2, (self.item_combo_rect.x + 6, self.item_combo_rect.y + 1))
                # Columns header
                ch_y = item_y + 24
                pygame.draw.rect(surf, (34, 34, 34), pygame.Rect(0, ch_y, w, 22))
                th_key = font.render("Key", True, (220, 220, 220))
                th_val = font.render("Value", True, (220, 220, 220))
                surf.blit(th_key, (10, ch_y + 3))
                self.value_col_x = int(getattr(model, 'value_col_x', 200) or 200)
                surf.blit(th_val, (self.value_col_x, ch_y + 3))
                # Rows area
                y_off = ch_y + 22
                visible_rows = max(0, (h - y_off - 8) // row_h)
                model.max_scroll = max(0, max(0, len(model.rows) - visible_rows))
                start = max(0, min(model.scroll, model.max_scroll))
                end = min(len(model.rows), start + visible_rows)
                for idx in range(start, end):
                    row = model.rows[idx]
                    row_y = y_off + (idx - start) * row_h
                    rect = pygame.Rect(6, row_y, w - 12, row_h)
                    if model.selected_index == idx:
                        pygame.draw.rect(surf, (60, 100, 160, 160), rect)
                    elif model.hovered_index == idx:
                        pygame.draw.rect(surf, (60, 60, 60, 100), rect)
                    color = (255, 255, 255) if model.selected_index == idx else (230, 230, 230)
                    key_surf = font.render(row.key, True, color)
                    val_text = row.value if (row.value is not None and row.value != "") else "<unset>"
                    v_color = (220, 220, 220) if val_text != "<unset>" else (180, 160, 160)
                    val_surf = font.render(val_text, True, v_color)
                    surf.blit(key_surf, (10, row_y + 3))
                    surf.blit(val_surf, (self.value_col_x, row_y + 3))
                    # Editing underline and inline text when editing
                    if model.editing_index == idx:
                        underline_rect = pygame.Rect(self.value_col_x - 2, row_y + row_h - 4, w - self.value_col_x - 10, 1)
                        pygame.draw.rect(surf, (120, 160, 220), underline_rect)
                        edit_txt = font.render(model.editing_text, True, (240, 240, 240))
                        surf.blit(edit_txt, (self.value_col_x, row_y + 3))
            except Exception:
                pass
            screen.blit(surf, self.panel_rect.topleft)
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.panel_rect)
            except Exception:
                pass
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["FsmPropertiesPanelView"]

