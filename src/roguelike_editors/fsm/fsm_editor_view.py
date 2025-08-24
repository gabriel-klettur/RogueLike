"""FSM Editor - Main View (skeleton)"""
from __future__ import annotations

from typing import Optional

from roguelike_editors.fsm.fsm_title.fsm_title_model import FsmTitleModel
from roguelike_editors.fsm.fsm_title.fsm_title_controller import FsmTitleController

class FsmEditorView:
    def __init__(self) -> None:
        self._title_ctrl: Optional[FsmTitleController] = None

    def render(self, controller, screen) -> None:
        if not getattr(controller, "visible", False):
            return
        # Panels are rendered by controller; the view handles shared chrome like titlebar.
        # Render FSM Editor Title using reusable TitleBar (same as other editors)
        try:
            if self._title_ctrl is None:
                self._title_ctrl = FsmTitleController(editor_state=None, model=FsmTitleModel(), font=None)
            self._title_ctrl.render(screen)
        except Exception:
            # Keep optional UI safe
            pass

__all__ = ["FsmEditorView"]
