from __future__ import annotations

from typing import Optional, List, Dict, Any

from .spawner_list_model import SpawnerListModel
from .spawner_list_view import SpawnerListView
from .spawner_list_events import SpawnerListEventHandler
from roguelike_editors.spawner.services.persistence import load_instances_json


class SpawnerListController:
    def __init__(self,
                 model: Optional[SpawnerListModel] = None,
                 view: Optional[SpawnerListView] = None) -> None:
        self.model = model or SpawnerListModel()
        self.view = view or SpawnerListView()
        self.events = SpawnerListEventHandler()
        # Raw instances cache corresponding to rows in model.items
        self._instances: List[Dict[str, Any]] = []

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        return self.events.handle_event(self, event)

    # --- Data ops ------------------------------------------------------------
    def refresh_from_disk(self) -> None:
        """Load spawner instances.json and fill model.items and cache raw entries."""
        data = load_instances_json()
        self._instances = data
        items: List[str] = []
        for inst in data:
            try:
                tpl = inst.get('template_id', '?')
                zone = inst.get('zone', '?')
                tile = inst.get('tile', [0, 0])
                items.append(f"{tpl} @ {zone} ({tile[0]},{tile[1]})")
            except Exception:
                items.append(str(inst))
        self.model.items = items
        # Clamp selection if out of range
        if self.model.selected_index is not None and not (0 <= self.model.selected_index < len(items)):
            self.model.selected_index = None

    def get_selected_instance(self) -> Optional[Dict[str, Any]]:
        idx = self.model.selected_index
        if idx is None:
            return None
        if 0 <= idx < len(self._instances):
            return self._instances[idx]
        return None


__all__ = ["SpawnerListController"]
