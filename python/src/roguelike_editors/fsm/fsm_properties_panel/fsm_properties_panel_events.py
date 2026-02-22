from __future__ import annotations


class FsmPropertiesPanelEventHandler:
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

        # Geometry constants (must mirror view)
        header_h = 30
        tabs_h = 24
        row_h = 22
        # Rows area offset in local panel coordinates
        y_off = header_h + tabs_h + 70

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
                # Tabs
                try:
                    if getattr(view, 'tabs_nodes_rect', None) and view.tabs_nodes_rect.move(rect.left, rect.top).collidepoint(pos):
                        controller._switch_tab('nodes')
                        return True
                    if getattr(view, 'tabs_trans_rect', None) and view.tabs_trans_rect.move(rect.left, rect.top).collidepoint(pos):
                        controller._switch_tab('transitions')
                        return True
                except Exception:
                    pass
                # Set navigation
                try:
                    if getattr(view, 'set_prev_rect', None) and view.set_prev_rect.move(rect.left, rect.top).collidepoint(pos):
                        controller._navigate_set(-1)
                        return True
                    if getattr(view, 'set_next_rect', None) and view.set_next_rect.move(rect.left, rect.top).collidepoint(pos):
                        controller._navigate_set(1)
                        return True
                except Exception:
                    pass
                # Item navigation
                try:
                    if getattr(view, 'item_prev_rect', None) and view.item_prev_rect.move(rect.left, rect.top).collidepoint(pos):
                        controller._navigate_item(-1)
                        return True
                    if getattr(view, 'item_next_rect', None) and view.item_next_rect.move(rect.left, rect.top).collidepoint(pos):
                        controller._navigate_item(1)
                        return True
                except Exception:
                    pass
                # Rows
                rel_y = pos[1] - rect.top
                if rel_y >= y_off:
                    local = (rel_y - y_off) // row_h
                    idx = start + int(local)
                    if 0 <= idx < len(model.rows):
                        model.selected_index = idx
                        # If click on value column, start editing
                        vx = rect.left + int(getattr(view, 'value_col_x', getattr(model, 'value_col_x', 200)))
                        if pos[0] >= vx:
                            row = model.rows[idx]
                            if getattr(row, 'editable', True):
                                model.editing_index = idx
                                model.editing_text = (row.value or "")
                        return True
                # Consume clicks inside panel even if not on rows
                return True

        # Editing keystrokes
        if getattr(model, 'editing_index', None) is not None:
            if et == pygame.KEYDOWN:
                key = getattr(event, 'key', None)
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
                # Basic text input
                ch = None
                try:
                    ch = event.unicode
                except Exception:
                    ch = None
                if ch and 32 <= ord(ch) < 127:
                    model.editing_text += ch
                    return True

        # Block mouse events inside panel bounds
        if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            if rect.collidepoint(pos):
                return True
        return False


__all__ = ["FsmPropertiesPanelEventHandler"]

