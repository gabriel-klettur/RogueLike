from __future__ import annotations


class ListPanelView:
    def __init__(self) -> None:
        self.panel_rect = None
        # Map of global row index -> pygame.Rect (panel-local) for the "@ zone (x,y)" prefix
        self.coords_hitboxes = {}

    def render(self, model, screen, *, anchor=(20, 120)):
        if not getattr(model, 'visible', True):
            return None
        try:
            import pygame  # type: ignore
            x, y = anchor
            width = int(getattr(model, 'panel_width', 720) or 720)
            height = 260
            self.panel_rect = pygame.Rect(x, y, width, height)
            surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            surf.fill((20, 20, 20, 220))
            pygame.draw.rect(surf, (90, 90, 90), surf.get_rect(), 2)
            # Header
            try:
                title_font = pygame.font.SysFont(None, 22)
                font = pygame.font.SysFont(None, 20)
                title_text = getattr(model, 'title', 'Spawners')
                title = title_font.render(str(title_text), True, (240, 240, 240))
                surf.blit(title, (10, 6))
                # Layout params
                header_h = int(getattr(model, 'header_height', 28) or 28)
                row_h = int(getattr(model, 'row_height', 20) or 20)
                visible_rows = int(getattr(model, 'visible_rows', 11) or 11)
                y_off = header_h
                items = list(getattr(model, 'items', []) or [])
                start = max(0, int(getattr(model, 'scroll_offset', 0) or 0))
                end = min(start + visible_rows, len(items))
                # reset hitboxes for this frame
                self.coords_hitboxes = {}
                # Rows (windowed by scroll_offset)
                for i, item in enumerate(items[start:end]):
                    g_idx = start + i
                    row_y = y_off + i * row_h
                    row_rect_local = pygame.Rect(6, row_y - 2, width - 12, row_h)
                    if getattr(model, 'selected_index', None) == g_idx:
                        pygame.draw.rect(surf, (60, 100, 160, 160), row_rect_local)
                    elif getattr(model, 'hovered_index', None) == g_idx:
                        pygame.draw.rect(surf, (60, 60, 60, 100), row_rect_local)
                    color = (255, 255, 255) if getattr(model, 'selected_index', None) == g_idx else (230, 230, 230)
                    text = font.render(str(item), True, color)
                    surf.blit(text, (10, row_y))
                    # If item begins with an "@ zone (x,y)" prefix, compute its hitbox and hover outline
                    try:
                        s = str(item)
                        if s.startswith('@ ') and ') ' in s:
                            end_par = s.find(')')
                            if end_par != -1:
                                prefix = s[: end_par + 1]
                                line_h = font.get_linesize()
                                prefix_w = font.size(prefix)[0]
                                seg_rect = pygame.Rect(10, row_y, max(prefix_w, 1), line_h)
                                self.coords_hitboxes[g_idx] = seg_rect
                                # Hover outline in orange if mouse over
                                try:
                                    mx, my = pygame.mouse.get_pos()
                                    local_x = mx - self.panel_rect.left
                                    local_y = my - self.panel_rect.top
                                    if seg_rect.collidepoint(local_x, local_y):
                                        pygame.draw.rect(surf, (255, 165, 0), seg_rect, 2)
                                except Exception:
                                    pass
                    except Exception:
                        pass
                    # Yellow outline for hover/selection
                    if getattr(model, 'hovered_index', None) == g_idx or getattr(model, 'selected_index', None) == g_idx:
                        pygame.draw.rect(surf, (255, 220, 60), row_rect_local, 2)
                # Simple scrollbar
                if len(items) > visible_rows:
                    track_rect = pygame.Rect(width - 10, y_off, 4, visible_rows * row_h)
                    pygame.draw.rect(surf, (70, 70, 70), track_rect)
                    frac = visible_rows / max(1, len(items))
                    thumb_h = max(10, int(track_rect.height * frac))
                    max_off = max(0, len(items) - visible_rows)
                    off_frac = (start / max_off) if max_off > 0 else 0.0
                    thumb_y = track_rect.y + int((track_rect.height - thumb_h) * off_frac)
                    thumb_rect = pygame.Rect(track_rect.x, thumb_y, track_rect.width, thumb_h)
                    pygame.draw.rect(surf, (160, 160, 160), thumb_rect)
            except Exception:
                pass
            screen.blit(surf, self.panel_rect.topleft)
            # Block gameplay input under panel
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.panel_rect)
            except Exception:
                pass
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["ListPanelView"]

