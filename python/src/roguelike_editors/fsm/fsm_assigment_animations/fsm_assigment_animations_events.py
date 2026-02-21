from __future__ import annotations


class FsmAssigmentAnimationsEventHandler:
    def handle_event(self, controller, event) -> bool:
        model = controller.model
        view = controller.view
        if not getattr(model, 'visible', False):
            return False
        if getattr(view, 'panel_rect', None) is None:
            return False
        try:
            import pygame  # type: ignore
        except Exception:
            return False

        rect = view.panel_rect
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None)
        if pos is None:
            try:
                pos = pygame.mouse.get_pos()
            except Exception:
                pos = (0, 0)

        # Geometry constants (must match view)
        header_h = 30
        ch_h = 22
        y_off = header_h + ch_h
        row_h = 22
        # Compute current scroll window
        try:
            w, h = rect.size
            visible_rows = max(0, (h - y_off - 8) // row_h)
            start = max(0, min(model.scroll, model.max_scroll))
        except Exception:
            visible_rows = 0
            start = 0

        # Hovering over rows
        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                rel_y = pos[1] - rect.top
                if rel_y >= y_off:
                    local = (rel_y - y_off) // row_h
                    idx = start + int(local)
                    if 0 <= idx < len(model.rows):
                        model.hovered_index = idx
                    else:
                        model.hovered_index = None
                    return True
            else:
                model.hovered_index = None
        # Scroll
        if et == pygame.MOUSEWHEEL:
            if rect.collidepoint(pos):
                model.scroll = max(0, min(model.max_scroll, model.scroll - int(getattr(event, 'y', 0))))
                return True
        # Clicks
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                # Header nav buttons
                if getattr(view, 'prev_rect', None) and view.prev_rect.move(rect.left, rect.top).collidepoint(pos):
                    controller._navigate_target(-1)
                    return True
                if getattr(view, 'next_rect', None) and view.next_rect.move(rect.left, rect.top).collidepoint(pos):
                    controller._navigate_target(1)
                    return True
                # Rows
                rel_y = pos[1] - rect.top
                if rel_y >= y_off:
                    local = (rel_y - y_off) // row_h
                    idx = start + int(local)
                    if 0 <= idx < len(model.rows):
                        model.selected_index = idx
                        # If click on value column, start editing
                        vx = rect.left + getattr(view, 'value_col_x', 180)
                        if pos[0] >= vx:
                            row = model.rows[idx]
                            model.editing_index = idx
                            model.editing_text = (row.value or "")
                        return True
                # Consume clicks inside panel even if not on rows
                return True
        # Editing keystrokes
        if getattr(model, 'editing_index', None) is not None:
            if et == pygame.KEYDOWN:
                key = getattr(event, 'key', None)
                mod = getattr(event, 'mod', 0)
                if key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    controller._commit_edit()
                    return True
                if key == pygame.K_ESCAPE:
                    # cancel edit
                    model.editing_index = None
                    model.editing_text = ""
                    return True
                if key == pygame.K_BACKSPACE:
                    model.editing_text = model.editing_text[:-1]
                    return True
                # Allow basic filename-ish characters
                ch = None
                try:
                    ch = event.unicode
                except Exception:
                    ch = None
                if ch and 32 <= ord(ch) < 127:
                    model.editing_text += ch
                    return True
        # Block mouse events when inside panel
        if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            if rect.collidepoint(pos):
                return True
        return False


__all__ = ["FsmAssigmentAnimationsEventHandler"]

