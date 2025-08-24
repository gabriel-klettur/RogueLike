from __future__ import annotations

from roguelike_editors.spawner.common.list_panel_events import ListPanelEventHandler


class ListTemplatesEventHandler(ListPanelEventHandler):
    """Extend base list events to handle per-row buttons.

    Buttons are provided by the view in `view.row_button_rects` after render().
    """

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
        # If delete confirmation modal is visible, route all events to it first
        try:
            dmodel = getattr(controller, 'delete_model', None)
            if getattr(dmodel, 'confirm_visible', False):
                return controller.delete_events.handle_modal_event(controller, event)
        except Exception:
            pass
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        # Check button clicks first on LMB down
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                for info in getattr(view, 'row_button_rects', []) or []:
                    gidx = info.get('gidx')
                    if gidx is None:
                        continue
                    if info.get('add') and info['add'].collidepoint(pos):
                        model.selected_index = int(gidx)
                        controller.add_template_at(int(gidx))
                        return True
                    if info.get('clone') and info['clone'].collidepoint(pos):
                        model.selected_index = int(gidx)
                        controller.clone_template_at(int(gidx))
                        return True
                    if info.get('delete') and info['delete'].collidepoint(pos):
                        model.selected_index = int(gidx)
                        # Open confirmation modal instead of deleting immediately
                        try:
                            return controller.delete_events.handle_button_click(controller, int(gidx))
                        except Exception:
                            return True
                        return True
        # Fallback to default behavior (hover/selection/scroll)
        return super().handle_event(controller, event)


__all__ = ["ListTemplatesEventHandler"]

