"""FSM Editor - Main View (skeleton)"""
from __future__ import annotations


class FsmEditorView:
    def render(self, controller, screen) -> None:
        if not getattr(controller, "visible", False):
            return
        # TODO: draw panels; reserve rects; register UI blockers
        return


__all__ = ["FsmEditorView"]
