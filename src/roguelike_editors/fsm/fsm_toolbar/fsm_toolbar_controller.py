from __future__ import annotations
from typing import Optional

from .fsm_toolbar_model import FsmToolbarModel
from .fsm_toolbar_view import FsmToolbarView


class FsmToolbarController:
    def __init__(self, model: Optional[FsmToolbarModel] = None, view: Optional[FsmToolbarView] = None) -> None:
        self.model = model or FsmToolbarModel()
        self.view = view or FsmToolbarView()

    def render(self, screen):
        return self.view.render(self.model, screen)

    def handle_event(self, event) -> bool:
        # TODO: click mapping to tools; keyboard shortcuts
        return False


__all__ = ["FsmToolbarController"]
