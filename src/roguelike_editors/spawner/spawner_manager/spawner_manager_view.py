from __future__ import annotations


class SpawnerManagerView:
    def render(self, controller, screen, *, anchor=None):
        # For now the manager is a thin container hosting the list panel
        if anchor is None:
            return controller.list_controller.render(screen)
        return controller.list_controller.render(screen, anchor=anchor)


__all__ = ["SpawnerManagerView"]
