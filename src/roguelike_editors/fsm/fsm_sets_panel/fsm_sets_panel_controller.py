from __future__ import annotations
from typing import Optional

from .fsm_sets_panel_model import FsmSetsPanelModel
from .fsm_sets_panel_view import FsmSetsPanelView


class FsmSetsPanelController:
    def __init__(self, model: Optional[FsmSetsPanelModel] = None, view: Optional[FsmSetsPanelView] = None) -> None:
        self.model = model or FsmSetsPanelModel()
        self.view = view or FsmSetsPanelView()

    def render(self, screen):
        return self.view.render(self.model, screen)

    def handle_event(self, event) -> bool:
        # TODO: delegate to PickerPanel, update selection
        return False


__all__ = ["FsmSetsPanelController"]
