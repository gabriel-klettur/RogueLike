from __future__ import annotations

from roguelike_editors.spawner.common.list_panel_view import ListPanelView


class ListTemplatesView(ListPanelView):
    """Custom view for Templates list with per-row action buttons.

    Exposes `panel_rect` (inherited) and `row_button_rects`:
    - row_button_rects: list of dicts per visible row: {'add': Rect, 'clone': Rect, 'delete': Rect, 'gidx': int}
    """

    def __init__(self) -> None:
        super().__init__()
        self.row_button_rects = []

    def render(self, model, screen, *, anchor=(20, 120), controller=None):
        rect = super().render(model, screen, anchor=anchor)
        # If not visible or failed to render, clear layouts
        self.row_button_rects = []
        if rect is None or not getattr(model, 'visible', True):
            return rect
        try:
            import pygame  # type: ignore
            # Recreate the same layout numbers as base
            x, y = rect.topleft
            width, height = rect.size
            header_h = int(getattr(model, 'header_height', 28) or 28)
            row_h = int(getattr(model, 'row_height', 20) or 20)
            visible_rows = int(getattr(model, 'visible_rows', 11) or 11)
            items = list(getattr(model, 'items', []) or [])
            start = max(0, int(getattr(model, 'scroll_offset', 0) or 0))
            end = min(start + visible_rows, len(items))

            try:
                blink_idx = getattr(model, '_blink_row_index', None)
                blink_end = getattr(model, '_blink_end_ticks', 0)
                now = pygame.time.get_ticks()
                if blink_idx is not None and now < int(blink_end):
                    if start <= int(blink_idx) < end:
                        i = int(blink_idx) - start
                        row_y = y + header_h + i * row_h
                        row_rect = pygame.Rect(x + 6, row_y - 2, width - 12, row_h)
                        phase_on = ((now // 120) % 2) == 0
                        if phase_on:
                            pygame.draw.rect(screen, (255, 230, 80), row_rect, 3)
                elif blink_idx is not None and now >= int(blink_end):
                    try:
                        setattr(model, '_blink_row_index', None)
                        setattr(model, '_blink_end_ticks', 0)
                    except Exception:
                        pass
            except Exception:
                pass

            # Draw per-row buttons on top of the already blitted panel
            # We will draw directly to the screen using absolute coordinates
            # Button sizes
            btn_w = 22
            btn_h = 16
            gap = 6
            right_margin = 12
            # Colors
            col_border = (200, 200, 200)
            col_text = (235, 235, 235)
            col_add = (50, 110, 60)
            col_clone = (60, 60, 110)
            col_del = (120, 50, 50)
            font = pygame.font.SysFont(None, 16)

            for i in range(end - start):
                g_idx = start + i
                row_y = y + header_h + i * row_h
                # Compute right-aligned button rects: [Add][Clone][Delete]
                delete_rect = pygame.Rect(x + width - right_margin - btn_w, row_y - 1, btn_w, btn_h)
                clone_rect = pygame.Rect(delete_rect.x - gap - btn_w, row_y - 1, btn_w, btn_h)
                add_rect = pygame.Rect(clone_rect.x - gap - btn_w, row_y - 1, btn_w, btn_h)
                self.row_button_rects.append({
                    'gidx': g_idx,
                    'add': add_rect,
                    'clone': clone_rect,
                    'delete': delete_rect,
                })
                # Draw buttons
                pygame.draw.rect(screen, col_add, add_rect)
                pygame.draw.rect(screen, col_border, add_rect, 1)
                pygame.draw.rect(screen, col_clone, clone_rect)
                pygame.draw.rect(screen, col_border, clone_rect, 1)
                pygame.draw.rect(screen, col_del, delete_rect)
                pygame.draw.rect(screen, col_border, delete_rect, 1)
                # Glyphs
                add_surf = font.render('+', True, col_text)
                screen.blit(add_surf, (add_rect.centerx - add_surf.get_width() // 2, add_rect.centery - add_surf.get_height() // 2))
                clone_surf = font.render('⧉', True, col_text)
                screen.blit(clone_surf, (clone_rect.centerx - clone_surf.get_width() // 2, clone_rect.centery - clone_surf.get_height() // 2))
                # Use an 'x' for delete to avoid font issues
                del_surf = font.render('x', True, col_text)
                screen.blit(del_surf, (delete_rect.centerx - del_surf.get_width() // 2, delete_rect.centery - del_surf.get_height() // 2))
        except Exception:
            # If drawing fails, keep rects empty so events ignore buttons
            self.row_button_rects = []
        # Render delete confirmation modal overlay if provided
        if controller is not None:
            try:
                controller.delete_view.render_modal(controller.delete_model, screen, rect)
            except Exception:
                pass
        return rect


__all__ = ["ListTemplatesView"]

