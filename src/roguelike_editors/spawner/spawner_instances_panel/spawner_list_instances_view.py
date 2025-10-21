from __future__ import annotations
from roguelike_editors.spawner.common.list_panel_view import ListPanelView


class SpawnerListInstancesView(ListPanelView):
    """Custom view for Instances list with per-row action buttons (duplicate/delete).

    Exposes `panel_rect` (inherited) and `row_button_rects`:
    - row_button_rects: list of dicts per visible row: {'dup': Rect, 'delete': Rect, 'gidx': int}
    """

    def __init__(self) -> None:
        super().__init__()
        self.row_button_rects = []

    def render(self, model, screen, *, anchor=(20, 120)):
        rect = super().render(model, screen, anchor=anchor)
        # Clear layouts if not visible or rendering failed
        self.row_button_rects = []
        if rect is None or not getattr(model, 'visible', True):
            return rect
        try:
            import pygame  # type: ignore
            # Mirror base layout numbers
            x, y = rect.topleft
            width, height = rect.size
            header_h = int(getattr(model, 'header_height', 28) or 28)
            row_h = int(getattr(model, 'row_height', 20) or 20)
            visible_rows = int(getattr(model, 'visible_rows', 11) or 11)
            items = list(getattr(model, 'items', []) or [])
            start = max(0, int(getattr(model, 'scroll_offset', 0) or 0))
            end = min(start + visible_rows, len(items))

            # Optional row blink (reuse same flags as Templates view)
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

            # Draw per-row buttons: [Duplicate][Delete] (right-aligned)
            btn_w = 22
            btn_h = 16
            gap = 6
            right_margin = 12
            # Colors and glyphs
            col_border = (200, 200, 200)
            col_text = (235, 235, 235)
            col_dup = (60, 60, 110)   # same as Templates 'clone'
            col_del = (120, 50, 50)
            font = pygame.font.SysFont(None, 16)

            for i in range(end - start):
                g_idx = start + i
                row_y = y + header_h + i * row_h
                delete_rect = pygame.Rect(x + width - right_margin - btn_w, row_y - 1, btn_w, btn_h)
                dup_rect = pygame.Rect(delete_rect.x - gap - btn_w, row_y - 1, btn_w, btn_h)
                self.row_button_rects.append({
                    'gidx': g_idx,
                    'dup': dup_rect,
                    'delete': delete_rect,
                })
                # Draw buttons
                pygame.draw.rect(screen, col_dup, dup_rect)
                pygame.draw.rect(screen, col_border, dup_rect, 1)
                pygame.draw.rect(screen, col_del, delete_rect)
                pygame.draw.rect(screen, col_border, delete_rect, 1)
                # Glyphs
                dup_surf = font.render('⧉', True, col_text)
                screen.blit(dup_surf, (dup_rect.centerx - dup_surf.get_width() // 2, dup_rect.centery - dup_surf.get_height() // 2))
                del_surf = font.render('x', True, col_text)
                screen.blit(del_surf, (delete_rect.centerx - del_surf.get_width() // 2, delete_rect.centery - del_surf.get_height() // 2))
        except Exception:
            self.row_button_rects = []
        return rect


__all__ = ["SpawnerListInstancesView"]
