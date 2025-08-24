from __future__ import annotations


class FsmSetsPanelEventHandler:
    def handle_event(self, controller, event) -> bool:
        # Consume interactions over panel; update hover/selection of items
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        model = controller.model
        view = controller.view
        if not getattr(model, 'visible', False):
            return False
        rect = getattr(view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()

        # When confirmation modal is visible, delegate all handling to delete events
        if getattr(getattr(controller, 'delete_model', None), 'confirm_visible', False):
            return controller.delete_events.handle_modal_event(controller, event)

        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                # Determine hovered button (clone/delete)
                try:
                    buttons = getattr(view, 'row_button_rects', {}) or {}
                except Exception:
                    buttons = {}
                hb_row = None
                hb_kind = None
                for i, rects in buttons.items():
                    try:
                        clone_r = rects.get('clone')
                        del_r = rects.get('delete')
                        if clone_r is not None and clone_r.collidepoint(pos):
                            hb_row, hb_kind = int(i), 'clone'
                            break
                        if del_r is not None and del_r.collidepoint(pos):
                            hb_row, hb_kind = int(i), 'delete'
                            break
                    except Exception:
                        continue
                model.hovered_button_row = hb_row
                model.hovered_button_kind = hb_kind
                # Hover index based on simple row layout
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(model.items):
                    model.hovered_index = int(index)
                else:
                    model.hovered_index = None
                return True
            else:
                # Clear hover states when outside panel
                model.hovered_index = None
                model.hovered_button_row = None
                model.hovered_button_kind = None
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                # First, check per-row action buttons
                try:
                    buttons = getattr(view, 'row_button_rects', {}) or {}
                except Exception:
                    buttons = {}
                # Iterate to find any hit
                for i, rects in buttons.items():
                    try:
                        clone_r = rects.get('clone')
                        del_r = rects.get('delete')
                        if clone_r is not None and clone_r.collidepoint(pos):
                            # Delegate to clone events handler
                            return controller.clone_events.handle_button_click(controller, int(i))
                        if del_r is not None and del_r.collidepoint(pos):
                            # Delegate delete button to delete events
                            return controller.delete_events.handle_button_click(controller, int(i))
                    except Exception:
                        continue
                # Otherwise treat as row selection
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(model.items):
                    model.selected_index = int(index)
                return True
        if et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            if rect.collidepoint(pos):
                return True
        return False


__all__ = ["FsmSetsPanelEventHandler"]
