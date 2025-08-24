from __future__ import annotations


class SpawnerManagerView:
    def render(self, controller, screen, *, anchor=None):
        # Render templates list on the left
        if anchor is None:
            list_rect = controller.list_controller.render(screen)
        else:
            list_rect = controller.list_controller.render(screen, anchor=anchor)
        # Render properties on the right when available
        props_rect = None
        try:
            if getattr(controller.props_controller.model, 'visible', False) and list_rect is not None:
                props_anchor = (list_rect.right + 8, list_rect.top)
                props_rect = controller.props_controller.render(screen, anchor=props_anchor)
        except Exception:
            pass
        # Optionally, return list_rect for compatibility; the props panel rect is stored in its own view
        return list_rect


__all__ = ["SpawnerManagerView"]
