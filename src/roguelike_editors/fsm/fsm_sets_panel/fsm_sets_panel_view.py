from __future__ import annotations


class FsmSetsPanelView:
    def __init__(self) -> None:
        self.panel_rect = None

    def render(self, model, screen, *, anchor=(20, 120)):
        if not getattr(model, "visible", False):
            return None
        # TODO: integrate PickerPanel; for now allocate a small rect
        try:
            import pygame  # type: ignore
            x, y = anchor
            self.panel_rect = pygame.Rect(x, y, 300, 240)
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["FsmSetsPanelView"]
