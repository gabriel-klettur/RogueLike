from __future__ import annotations


class FsmSetsPanelView:
    def __init__(self) -> None:
        self.panel_rect = None
        self.row_button_rects = {}
        self.confirm_rect = None
        self.confirm_yes_rect = None
        self.confirm_no_rect = None

    def render(self, model, screen, *, anchor=(20, 120)):
        if not getattr(model, "visible", False):
            return None
        # TODO: integrate PickerPanel; for now allocate a small rect
        try:
            import pygame  # type: ignore
            x, y = anchor
            self.panel_rect = pygame.Rect(x, y, 300, 240)
            # Draw simple panel bg and items
            surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            surf.fill((20, 20, 20, 220))
            border = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            pygame.draw.rect(border, (90, 90, 90), border.get_rect(), 2)
            surf.blit(border, (0, 0))
            try:
                font = pygame.font.SysFont(None, 20)
                small_font = pygame.font.SysFont(None, 16)
                # Title
                title_font = pygame.font.SysFont(None, 22)
                title = title_font.render("FSM Sets", True, (240, 240, 240))
                surf.blit(title, (10, 6))
                y_off = 28
                # Reset per-render button rects
                self.row_button_rects = {}
                # Rows (limit to 10 for current fixed panel height)
                for i, item in enumerate(getattr(model, 'items', [])[:10]):
                    row_y = y_off + i * 20
                    # Highlight row if hovered/selected
                    if model.selected_index == i:
                        pygame.draw.rect(surf, (60, 100, 160, 160), pygame.Rect(6, row_y - 2, self.panel_rect.width - 12, 20))
                    elif model.hovered_index == i:
                        pygame.draw.rect(surf, (60, 60, 60, 100), pygame.Rect(6, row_y - 2, self.panel_rect.width - 12, 20))
                    color = (255, 255, 255) if model.selected_index == i else (230, 230, 230)
                    text = font.render(f"• {item}", True, color)
                    surf.blit(text, (10, row_y))
                    # Per-row action buttons (clone / delete)
                    btn_size = 16
                    gap = 4
                    delete_local_x = self.panel_rect.width - 10 - btn_size
                    clone_local_x = delete_local_x - gap - btn_size
                    btn_y = row_y - 1  # center within 20px row
                    # Clone button
                    clone_rect_local = pygame.Rect(clone_local_x, btn_y, btn_size, btn_size)
                    pygame.draw.rect(surf, (40, 80, 120), clone_rect_local, border_radius=3)
                    pygame.draw.rect(surf, (20, 40, 60), clone_rect_local, 1, border_radius=3)
                    c_label = small_font.render("C", True, (240, 240, 240))
                    surf.blit(c_label, (clone_local_x + (btn_size - c_label.get_width()) // 2, btn_y + (btn_size - c_label.get_height()) // 2))
                    # Delete button
                    delete_rect_local = pygame.Rect(delete_local_x, btn_y, btn_size, btn_size)
                    pygame.draw.rect(surf, (140, 60, 60), delete_rect_local, border_radius=3)
                    pygame.draw.rect(surf, (80, 30, 30), delete_rect_local, 1, border_radius=3)
                    d_label = small_font.render("X", True, (240, 240, 240))
                    surf.blit(d_label, (delete_local_x + (btn_size - d_label.get_width()) // 2, btn_y + (btn_size - d_label.get_height()) // 2))
                    # Hover/Selected outlines (yellow) for row and buttons
                    try:
                        yellow = (255, 220, 60)
                        # Row outline when hovered or selected
                        if getattr(model, 'hovered_index', None) == i or getattr(model, 'selected_index', None) == i:
                            pygame.draw.rect(surf, yellow, pygame.Rect(6, row_y - 2, self.panel_rect.width - 12, 20), 2)
                        # Button outlines when hovered specific button
                        if getattr(model, 'hovered_button_row', None) == i:
                            if getattr(model, 'hovered_button_kind', None) == 'clone':
                                pygame.draw.rect(surf, yellow, clone_rect_local, 2, border_radius=3)
                            elif getattr(model, 'hovered_button_kind', None) == 'delete':
                                pygame.draw.rect(surf, yellow, delete_rect_local, 2, border_radius=3)
                    except Exception:
                        pass
                    # Store screen-space rects for hit-testing
                    clone_rect_screen = pygame.Rect(self.panel_rect.left + clone_local_x, self.panel_rect.top + btn_y, btn_size, btn_size)
                    delete_rect_screen = pygame.Rect(self.panel_rect.left + delete_local_x, self.panel_rect.top + btn_y, btn_size, btn_size)
                    self.row_button_rects[i] = {
                        'clone': clone_rect_screen,
                        'delete': delete_rect_screen,
                    }
            except Exception:
                pass
            screen.blit(surf, self.panel_rect.topleft)
            # Register blocker to suppress gameplay input under the panel
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.panel_rect)
            except Exception:
                pass
            # Confirmation modal overlay (draw after panel so it appears on top)
            try:
                if getattr(model, 'confirm_visible', False):
                    overlay = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
                    overlay.fill((0, 0, 0, 120))
                    screen.blit(overlay, self.panel_rect.topleft)
                    # Dialog rect centered within panel
                    dw, dh = 260, 110
                    dx = self.panel_rect.left + (self.panel_rect.width - dw) // 2
                    dy = self.panel_rect.top + (self.panel_rect.height - dh) // 2
                    self.confirm_rect = pygame.Rect(dx, dy, dw, dh)
                    # Draw dialog bg
                    dlg = pygame.Surface((dw, dh), pygame.SRCALPHA)
                    dlg.fill((30, 30, 30, 240))
                    pygame.draw.rect(dlg, (160, 160, 160), dlg.get_rect(), 2, border_radius=4)
                    # Text (wrap simple)
                    try:
                        font = pygame.font.SysFont(None, 18)
                        msg = getattr(model, 'confirm_text', '') or 'Confirm?'
                        lines = []
                        words = msg.split()
                        line = ''
                        max_w = dw - 20
                        for w in words:
                            test = (line + ' ' + w).strip()
                            if font.size(test)[0] <= max_w:
                                line = test
                            else:
                                if line:
                                    lines.append(line)
                                line = w
                        if line:
                            lines.append(line)
                        ty = 12
                        for ln in lines[:3]:  # cap lines
                            text_s = font.render(ln, True, (240, 240, 240))
                            dlg.blit(text_s, (10, ty))
                            ty += text_s.get_height() + 4
                    except Exception:
                        pass
                    # Buttons
                    bw, bh = 84, 26
                    spacing = 16
                    by = dh - bh - 12
                    bx_yes = (dw - (2 * bw + spacing)) // 2
                    bx_no = bx_yes + bw + spacing
                    yes_rect_local = pygame.Rect(bx_yes, by, bw, bh)
                    no_rect_local = pygame.Rect(bx_no, by, bw, bh)
                    pygame.draw.rect(dlg, (60, 120, 60), yes_rect_local, border_radius=3)
                    pygame.draw.rect(dlg, (30, 60, 30), yes_rect_local, 1, border_radius=3)
                    pygame.draw.rect(dlg, (120, 60, 60), no_rect_local, border_radius=3)
                    pygame.draw.rect(dlg, (60, 30, 30), no_rect_local, 1, border_radius=3)
                    try:
                        btn_font = pygame.font.SysFont(None, 18)
                        yes_t = btn_font.render("Yes", True, (240, 240, 240))
                        no_t = btn_font.render("No", True, (240, 240, 240))
                        dlg.blit(yes_t, (yes_rect_local.x + (bw - yes_t.get_width()) // 2, yes_rect_local.y + (bh - yes_t.get_height()) // 2))
                        dlg.blit(no_t, (no_rect_local.x + (bw - no_t.get_width()) // 2, no_rect_local.y + (bh - no_t.get_height()) // 2))
                    except Exception:
                        pass
                    screen.blit(dlg, (dx, dy))
                    # Store screen-space rects
                    import pygame as _pg  # alias to build Rect safely even if previous try block fails
                    self.confirm_yes_rect = _pg.Rect(dx + yes_rect_local.x, dy + yes_rect_local.y, bw, bh)
                    self.confirm_no_rect = _pg.Rect(dx + no_rect_local.x, dy + no_rect_local.y, bw, bh)
                    # Block input behind modal
                    try:
                        from roguelike_ui.ui_blocker import register_blocker
                        register_blocker(self.confirm_rect)
                    except Exception:
                        pass
                else:
                    self.confirm_rect = None
                    self.confirm_yes_rect = None
                    self.confirm_no_rect = None
            except Exception:
                # On any failure, ensure rects are cleared to avoid stale references
                self.confirm_rect = None
                self.confirm_yes_rect = None
                self.confirm_no_rect = None
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["FsmSetsPanelView"]
