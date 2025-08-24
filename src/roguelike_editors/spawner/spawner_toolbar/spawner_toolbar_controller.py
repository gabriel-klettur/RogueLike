from __future__ import annotations

from typing import Optional
import logging

from .spawner_toolbar_model import SpawnerToolbarModel
from .spawner_toolbar_view import SpawnerToolbarView
from .spawner_toolbar_events import SpawnerToolbarEventHandler


class SpawnerToolbarController:
    def __init__(self, editor_controller, model: Optional[SpawnerToolbarModel] = None,
                 view: Optional[SpawnerToolbarView] = None,
                 events: Optional[SpawnerToolbarEventHandler] = None) -> None:
        self.editor_controller = editor_controller
        self.model = model or SpawnerToolbarModel()
        self.view = view or SpawnerToolbarView()
        self.events = events or SpawnerToolbarEventHandler()

    def render(self, screen, *, anchor=None):
        return self.view.render(self.model, screen, anchor=anchor)

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

    # Callbacks invoked by events handler
    def on_undo(self):
        try:
            undo = getattr(self.editor_controller, 'undo', None)
            if callable(undo):
                undo()
            else:
                logging.getLogger(__name__).debug("[SpawnerToolbar] undo() not available on editor_controller")
        except Exception:
            logging.getLogger(__name__).debug("[SpawnerToolbar] undo() failed or no-op", exc_info=False)

    def on_redo(self):
        try:
            redo = getattr(self.editor_controller, 'redo', None)
            if callable(redo):
                redo()
            else:
                logging.getLogger(__name__).debug("[SpawnerToolbar] redo() not available on editor_controller")
        except Exception:
            logging.getLogger(__name__).debug("[SpawnerToolbar] redo() failed or no-op", exc_info=False)


__all__ = ["SpawnerToolbarController"]
