from __future__ import annotations

from typing import Optional

from .spawners_manager_model import SpawnersManagerModel
from .spawners_manager_view import SpawnersManagerView
from .spawners_manager_events import SpawnersManagerEventHandler


class SpawnersManagerController:
    def __init__(self,
                 model: Optional[SpawnersManagerModel] = None,
                 view: Optional[SpawnersManagerView] = None) -> None:
        self.model = model or SpawnersManagerModel()
        self.view = view or SpawnersManagerView()
        self.events = SpawnersManagerEventHandler()

    # --- API -----------------------------------------------------------------
    def set_template(self, tpl: Optional[dict]) -> None:
        self.model.selected_template = tpl
        self.model.visible = tpl is not None
        # Reset scroll when selection changes
        self.model.scroll_offset = 0

    def render(self, screen, *, anchor=None):
        if not self.model.visible:
            return None
        return self.view.render(self, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)


__all__ = ["SpawnersManagerController"]
