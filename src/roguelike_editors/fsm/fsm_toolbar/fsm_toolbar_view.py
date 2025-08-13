from __future__ import annotations


class FsmToolbarView:
    def __init__(self) -> None:
        self.last_rect = None  # placeholder for layout rect

    def render(self, model, screen, *, anchor=(20, 60)):
        if not getattr(model, "visible", True):
            return None
        # TODO: integrate ToolbarView from roguelike_ui.widgets.toolbar_panel
        # For now, only reserve a dummy rect and return it to the controller
        try:
            import pygame  # type: ignore
            x, y = anchor
            self.last_rect = pygame.Rect(x, y, 40, 40)
        except Exception:
            self.last_rect = None
        return self.last_rect


__all__ = ["FsmToolbarView"]
