from __future__ import annotations
from typing import Optional

from .fsm_toolbar_model import FsmToolbarModel
from .fsm_toolbar_view import FsmToolbarView
from .fsm_toolbar_events import FsmToolbarEventHandler


class FsmToolbarController:
    def __init__(self, model: Optional[FsmToolbarModel] = None, view: Optional[FsmToolbarView] = None) -> None:
        self.model = model or FsmToolbarModel()
        self.view = view or FsmToolbarView()
        self.events = FsmToolbarEventHandler()

    def render(self, screen):
        return self.view.render(self.model, screen)

    def handle_event(self, event) -> bool:
        consumed = False
        # Ensure toolbar is constructed for hit-testing during event phase
        try:
            ensure = getattr(self.view, 'ensure_ready', None)
            if ensure:
                ensure(self.model)
        except Exception:
            pass
        # Allow dragging the toolbar panel (RMB) via internal view toolbar if available
        toolbar = getattr(self.view, "toolbar", None)
        if toolbar is not None:
            try:
                consumed = bool(toolbar.handle_event(event)) or consumed
            except Exception:
                pass
        # Handle clicks/shortcuts for activating tools
        consumed = self.events.handle_event(self, event) or consumed
        return consumed

    # API used by ToolbarView to draw selection border
    def is_active(self, tool: str) -> bool:
        return getattr(self.model, "active_tool", None) == tool

    def set_active(self, tool: Optional[str]) -> None:
        self.model.active_tool = tool


__all__ = ["FsmToolbarController"]
