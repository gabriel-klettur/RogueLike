from __future__ import annotations


class FsmGraphPanelView:
    def __init__(self) -> None:
        self.canvas_rect = None

    def render(self, model, screen, *, anchor=(360, 120)):
        if not getattr(model, "visible", True):
            return None
        # TODO: draw nodes/edges with pan/zoom; for now return a placeholder rect
        try:
            import pygame  # type: ignore
            x, y = anchor
            self.canvas_rect = pygame.Rect(x, y, 800, 520)
        except Exception:
            self.canvas_rect = None
        return self.canvas_rect


__all__ = ["FsmGraphPanelView"]
