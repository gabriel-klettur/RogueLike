from __future__ import annotations


class FsmSetsPanelView:
    def __init__(self) -> None:
        self.panel_rect = None
        self.row_button_rects = {}

    def render(self, model, screen, *, anchor=(20, 120), controller=None):
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
                    # Warning badge for highlighted set with linter warnings
                    try:
                        if getattr(model, 'highlighted_set_id', None) == item and getattr(model, 'highlighted_warnings', []) and len(model.highlighted_warnings) > 0:
                            # Small amber circle with count, placed after text
                            amber = (255, 180, 60)
                            badge_d = 14
                            bx = 10 + text.get_width() + 6
                            by = row_y + (text.get_height() - badge_d) // 2
                            pygame.draw.circle(surf, (35, 24, 0), (bx + badge_d // 2, by + badge_d // 2), badge_d // 2)
                            pygame.draw.circle(surf, amber, (bx + badge_d // 2, by + badge_d // 2), badge_d // 2, width=2)
                            # Count (max 9+)
                            count = min(9, len(model.highlighted_warnings))
                            lbl = small_font.render(str(count), True, amber)
                            surf.blit(lbl, (bx + (badge_d - lbl.get_width()) // 2, by + (badge_d - lbl.get_height()) // 2))
                    except Exception:
                        pass
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
            # Confirmation modal overlay delegated to delete view
            if controller is not None:
                try:
                    controller.delete_view.render_modal(controller.delete_model, screen, self.panel_rect)
                except Exception:
                    pass
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["FsmSetsPanelView"]
