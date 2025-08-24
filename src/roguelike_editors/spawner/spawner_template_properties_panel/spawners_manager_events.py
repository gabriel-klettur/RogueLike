from __future__ import annotations


class SpawnersManagerEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        model = controller.model
        if not getattr(model, 'visible', False):
            return False
        rect = getattr(controller.view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()

        # Helpers for rows/geometry
        y_off = 30
        row_h = 20
        rows = controller.get_rows()
        scroll = int(getattr(model, 'scroll_offset', 0) or 0)
        viewport_h = int(rect.height - 38)  # y_off=30, bottom pad=8

        def compute_row_index(py):
            if not rect.collidepoint((rect.left + 1, py)):
                return None
            local_y = py - rect.top
            if local_y < y_off or local_y > (y_off + viewport_h):
                return None
            i = (local_y - y_off + scroll) // row_h
            gi = int(i)
            if 0 <= gi < len(rows):
                return gi
            return None

        if et == pygame.MOUSEWHEEL and rect.collidepoint(pos):
            # Scroll by row height (20px per wheel notch)
            dy = int(getattr(event, 'y', 0) or 0)
            current = int(getattr(model, 'scroll_offset', 0) or 0)
            # Positive y means wheel up -> move content down -> decrease offset
            new_offset = current - dy * 20
            # Clamp based on content height and viewport height
            content_h = int(getattr(controller.view, 'content_height', 0) or 0)
            panel = getattr(controller.view, 'panel_rect', None)
            if panel is not None:
                viewport_h = int(panel.height - 38)  # y_off=30, bottom padding=8
            else:
                viewport_h = 0
            max_scroll = max(0, content_h - max(0, viewport_h))
            model.scroll_offset = max(0, min(max_scroll, new_offset))
            return True

        # Hover tracking
        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                gi = compute_row_index(pos[1])
                model.hovered_index = gi
                return True
            else:
                model.hovered_index = None

        # If editing, route to text input first
        if controller.is_editing():
            # ESC: cancel editing
            if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
                ti = controller.get_text_input()
                if ti is not None:
                    ti.deactivate()
                model.editing_key = None
                model.editing_row_index = None
                return True
            ti = controller.get_text_input()
            # Transform mouse events to panel-local coordinates for TextInput
            handled = False
            if ti is not None:
                if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEMOTION):
                    local = (pos[0] - rect.left, pos[1] - rect.top)
                    payload = {k: getattr(event, k) for k in ('button','rel','x','y') if hasattr(event, k)}
                    payload['pos'] = local
                    fake = pygame.event.Event(et, payload)
                    handled = ti.handle_event(fake)
                else:
                    handled = ti.handle_event(event)
            if handled:
                controller.commit_edit_if_finished()
                return True
            # Click outside the input -> commit edit (focus loss)
            if et == pygame.MOUSEBUTTONDOWN:
                ti = controller.get_text_input()
                if ti is not None:
                    ti_rect_screen = None
                    try:
                        ti_rect_screen = pygame.Rect(rect.left + ti.last_rect.x,
                                                     rect.top + ti.last_rect.y,
                                                     ti.last_rect.width,
                                                     ti.last_rect.height)
                    except Exception:
                        ti_rect_screen = None
                    if ti_rect_screen is None or not ti_rect_screen.collidepoint(pos):
                        ti.deactivate()
                        controller.commit_edit_if_finished()
                        return True

        # Consume mouse clicks inside the panel (future: editing)
        if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and rect.collidepoint(pos):
            # Double-click to enter edit mode on a row
            if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                gi = compute_row_index(pos[1])
                if gi is not None:
                    # Double click detector key by row index
                    if controller._dbl.is_double_click(('prop', gi)):
                        controller.begin_edit_row(gi)
                        return True
            return True

        return False


__all__ = ["SpawnersManagerEventHandler"]
