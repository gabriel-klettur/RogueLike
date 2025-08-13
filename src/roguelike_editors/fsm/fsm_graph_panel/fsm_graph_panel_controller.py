from __future__ import annotations
from typing import Optional

from .fsm_graph_panel_model import FsmGraphPanelModel
from .fsm_graph_panel_view import FsmGraphPanelView


class FsmGraphPanelController:
    def __init__(self, model: Optional[FsmGraphPanelModel] = None, view: Optional[FsmGraphPanelView] = None) -> None:
        self.model = model or FsmGraphPanelModel()
        self.view = view or FsmGraphPanelView()

    def render(self, screen):
        return self.view.render(self.model, screen)

    def handle_event(self, event) -> bool:
        # TODO: pan/zoom/select/connect logic
        return False


__all__ = ["FsmGraphPanelController"]
