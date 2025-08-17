from __future__ import annotations

from typing import Optional, List, Dict, Any

from roguelike_editors.spawner.spawner_list_common import (
    ListPanelModel as SpawnerListInstancesModel,
    ListPanelView as SpawnerListInstancesView,
    ListPanelEventHandler as SpawnerListInstancesEventHandler,
)
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services.persistence import load_instances_json, zone_for_global_tile


class SpawnerListInstancesController:
    def __init__(self,
                 model: Optional[SpawnerListInstancesModel] = None,
                 view: Optional[SpawnerListInstancesView] = None) -> None:
        self.model = model or SpawnerListInstancesModel()
        # Show a specific title when used as the Instances list
        try:
            self.model.title = "Spawner Instances"
        except Exception:
            pass
        self.view = view or SpawnerListInstancesView()
        self.events = SpawnerListInstancesEventHandler()
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
                # Validate zone by recomputing from global coords
                warn = ""
                try:
                    ox, oy = global_map_settings.zone_offsets.get(zone, (0, 0))
                    gx, gy = int(ox) + int(tile[0]), int(oy) + int(tile[1])
                    computed = zone_for_global_tile(gx, gy)
                    if computed and str(computed) != str(zone):
                        warn = f" [zone mismatch -> {computed}]"
                except Exception:
                    pass
                items.append(f"{tpl} @ {zone} ({tile[0]},{tile[1]}){warn}")
            except Exception:
                items.append(str(inst))
        self.model.items = items
        # Clamp selection if out of range
        if self.model.selected_index is not None and not (0 <= self.model.selected_index < len(items)):
            self.model.selected_index = None
        # Clamp scroll window
        visible_rows = int(getattr(self.model, 'visible_rows', 11) or 11)
        max_off = max(0, len(items) - visible_rows)
        off = int(getattr(self.model, 'scroll_offset', 0) or 0)
        if off > max_off:
            self.model.scroll_offset = max_off
        if off < 0:
            self.model.scroll_offset = 0
        # Reset hover if invalid
        if self.model.hovered_index is not None and not (0 <= self.model.hovered_index < len(items)):
            self.model.hovered_index = None

    def get_selected_instance(self) -> Optional[Dict[str, Any]]:
        idx = self.model.selected_index
        if idx is None:
            return None
        if 0 <= idx < len(self._instances):
            return self._instances[idx]
        return None


__all__ = ["SpawnerListInstancesController"]
