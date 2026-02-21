from __future__ import annotations


class FsmAssigmentAnimationsView:
    def __init__(self) -> None:
        self.panel_rect = None
        self.header_rect = None
        self.prev_rect = None
        self.next_rect = None
        self.value_col_x = 180  # split column for value editing

    def render(self, model, screen, *, anchor=(20, 120)):
        if not getattr(model, "visible", False):
            return None
        try:
            import pygame  # type: ignore
            x, y = anchor
            w, h = 420, 320
            self.panel_rect = pygame.Rect(x, y, w, h)
            surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            surf.fill((20, 20, 20, 220))
            # Border (yellow when open)
            pygame.draw.rect(surf, (240, 210, 60), surf.get_rect(), 2)
            # Header with target navigation
            try:
                title_font = pygame.font.SysFont(None, 22)
                font = pygame.font.SysFont(None, 18)
                header_h = 30
                self.header_rect = pygame.Rect(0, 0, w, header_h)
                pygame.draw.rect(surf, (28, 28, 28), self.header_rect)
                label = title_font.render("Animations", True, (240, 240, 240))
                surf.blit(label, (10, 6))
                # Prev/Next arrows and current target id
                tx = 130
                ty = 6
                self.prev_rect = pygame.Rect(tx, ty, 20, 20)
                self.next_rect = pygame.Rect(tx + 20 + 180, ty, 20, 20)
                pygame.draw.rect(surf, (70, 70, 70), self.prev_rect, 1)
                pygame.draw.rect(surf, (70, 70, 70), self.next_rect, 1)
                # Draw arrows
                pygame.draw.polygon(surf, (200, 200, 200), [(tx + 14, ty + 4), (tx + 6, ty + 10), (tx + 14, ty + 16)])
                nx = self.next_rect.x
                ny = self.next_rect.y
                pygame.draw.polygon(surf, (200, 200, 200), [(nx + 6, ny + 4), (nx + 14, ny + 10), (nx + 6, ny + 16)])
                # Target text
                tgt = model.target_set_id or "default"
                tgt_label = font.render(f"Target: {tgt}", True, (230, 230, 230))
                surf.blit(tgt_label, (tx + 26, ty + 2))
                # Columns header
                ch_y = header_h
                pygame.draw.rect(surf, (34, 34, 34), pygame.Rect(0, ch_y, w, 22))
                th_state = font.render("State Class", True, (220, 220, 220))
                th_val = font.render("Animation Base", True, (220, 220, 220))
                surf.blit(th_state, (10, ch_y + 3))
                surf.blit(th_val, (self.value_col_x, ch_y + 3))
                # Rows area
                y_off = ch_y + 22
                row_h = 22
                visible_rows = max(0, (h - y_off - 8) // row_h)
                model.max_scroll = max(0, max(0, len(model.rows) - visible_rows))
                start = max(0, min(model.scroll, model.max_scroll))
                end = min(len(model.rows), start + visible_rows)
                for idx in range(start, end):
                    row = model.rows[idx]
                    row_y = y_off + (idx - start) * row_h
                    rect = pygame.Rect(6, row_y, w - 12, row_h)
                    # Highlights
                    if model.selected_index == idx:
                        pygame.draw.rect(surf, (60, 100, 160, 160), rect)
                    elif model.hovered_index == idx:
                        pygame.draw.rect(surf, (60, 60, 60, 100), rect)
                    # Draw state class
                    color = (255, 255, 255) if model.selected_index == idx else (230, 230, 230)
                    t_state = font.render(row.state_class, True, color)
                    surf.blit(t_state, (10, row_y + 3))
                    # Draw value
                    v_color = (220, 220, 220)
                    if row.value is None:
                        # Inherited or missing
                        if row.inherited:
                            val_txt = font.render("<inherit>", True, (170, 200, 170))
                        else:
                            val_txt = font.render("<unset>", True, (180, 160, 160))
                    else:
                        val_txt = font.render(row.value, True, v_color)
                    surf.blit(val_txt, (self.value_col_x, row_y + 3))
                    # If editing this row, draw caret box
                    if model.editing_index == idx:
                        # Simple underline to indicate editing
                        underline_rect = pygame.Rect(self.value_col_x - 2, row_y + row_h - 4, w - self.value_col_x - 10, 1)
                        pygame.draw.rect(surf, (120, 160, 220), underline_rect)
                        # Ghost typed text
                        edit_txt = font.render(model.editing_text, True, (240, 240, 240))
                        surf.blit(edit_txt, (self.value_col_x, row_y + 3))
            except Exception:
                pass
            screen.blit(surf, self.panel_rect.topleft)
            # Blocker
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.panel_rect)
            except Exception:
                pass
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["FsmAssigmentAnimationsView"]

