from __future__ import annotations

from typing import Optional

from .spawner_manager_model import SpawnerManagerModel
from .spawner_manager_view import SpawnerManagerView
from .spawner_manager_events import SpawnerManagerEventHandler
from .spawner_templates_list_controller import SpawnerTemplatesListController


class SpawnerManagerController:
    def __init__(self,
                 model: Optional[SpawnerManagerModel] = None,
                 view: Optional[SpawnerManagerView] = None) -> None:
        self.model = model or SpawnerManagerModel()
        self.view = view or SpawnerManagerView()
        self.events = SpawnerManagerEventHandler()
        # Child panels: list of templates from data/spawners/spawners.json
        self.list_controller = SpawnerTemplatesListController()
        # Track first-time activation to refresh data
        self._was_visible = False

    def set_visible(self, visible: bool) -> None:
        if visible and not self.model.visible:
            # Became visible -> refresh list from disk
            try:
                self.list_controller.refresh_from_disk()
            except Exception:
                pass
        self.model.visible = visible

    def render(self, screen, *, anchor=None):
        if not self.model.visible:
            return None
        return self.view.render(self, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)


__all__ = ["SpawnerManagerController"]
