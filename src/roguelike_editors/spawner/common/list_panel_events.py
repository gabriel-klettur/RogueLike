from __future__ import annotations


class ListPanelEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        model = controller.model
        view = controller.view
        if not getattr(model, 'visible', True):
            return False
        rect = getattr(view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        header_h = int(getattr(model, 'header_height', 28) or 28)
        row_h = int(getattr(model, 'row_height', 20) or 20)
        visible_rows = int(getattr(model, 'visible_rows', 11) or 11)
        items = list(getattr(model, 'items', []) or [])
        max_off = max(0, len(items) - visible_rows)
        # Helper: compute global index from mouse y
        def compute_gidx(py):
            local_y = py - rect.top
            if local_y < header_h:
                return None
            i = (local_y - header_h) // row_h
            if i < 0 or i >= visible_rows:
                return None
            g_idx = int(getattr(model, 'scroll_offset', 0) or 0) + int(i)
            if 0 <= g_idx < len(items):
                return g_idx
            return None

        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                gidx = compute_gidx(pos[1])
                model.hovered_index = gidx
                return True
            else:
                model.hovered_index = None
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                gidx = compute_gidx(pos[1])
                if gidx is not None:
                    model.selected_index = int(gidx)
                return True
        if et == pygame.MOUSEWHEEL:
            # Scroll only if mouse over panel
            if rect.collidepoint(pos):
                dy = int(getattr(event, 'y', 0) or 0)
                # pygame MOUSEWHEEL: y>0 means scroll up -> decrease offset
                new_off = int(getattr(model, 'scroll_offset', 0) or 0) - dy
                if new_off < 0:
                    new_off = 0
                if new_off > max_off:
                    new_off = max_off
                if new_off != getattr(model, 'scroll_offset', 0):
                    model.scroll_offset = new_off
                return True
        if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            if rect.collidepoint(pos):
                return True
        return False


__all__ = ["ListPanelEventHandler"]
