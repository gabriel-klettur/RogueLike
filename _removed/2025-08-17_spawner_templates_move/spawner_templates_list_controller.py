from __future__ import annotations

from typing import Optional, List, Dict, Any

from roguelike_editors.spawner.spawner_list_common import (
    ListPanelModel as SpawnerListInstancesModel,
    ListPanelView as SpawnerListInstancesView,
    ListPanelEventHandler as SpawnerListInstancesEventHandler,
)
from roguelike_editors.spawner.services.persistence import load_spawners_json


class SpawnerTemplatesListController:
    """List controller for spawner templates (spawners.json).

    Reuses the same view and event handler as the instances list to keep style consistent.
    """

    def __init__(self,
                 model: Optional[SpawnerListInstancesModel] = None,
                 view: Optional[SpawnerListInstancesView] = None) -> None:
        self.model = model or SpawnerListInstancesModel()
        # Show a specific title when used as the Templates list
        try:
            self.model.title = "Spawners Templates"
        except Exception:
            pass
        self.view = view or SpawnerListInstancesView()
        self.events = SpawnerListInstancesEventHandler()
        self._templates: List[Dict[str, Any]] = []

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        return self.events.handle_event(self, event)

    # --- Data ops ------------------------------------------------------------
    def refresh_from_disk(self) -> None:
        data = load_spawners_json()
        self._templates = data
        items: List[str] = []
        for sp in data:
            try:
                sid = sp.get('id', '?')
                stype = sp.get('spawner_type', '?')
                trig = sp.get('trigger', {})
                ttype = trig.get('type', '?') if isinstance(trig, dict) else '?'
                items.append(f"{sid} ({stype}, trigger={ttype})")
            except Exception:
                items.append(str(sp))
        self.model.items = items
        if self.model.selected_index is not None and not (0 <= self.model.selected_index < len(items)):
            self.model.selected_index = None

    def get_selected_template(self) -> Optional[Dict[str, Any]]:
        idx = self.model.selected_index
        if idx is None:
            return None
        if 0 <= idx < len(self._templates):
            return self._templates[idx]
        return None


__all__ = ["SpawnerTemplatesListController"]
