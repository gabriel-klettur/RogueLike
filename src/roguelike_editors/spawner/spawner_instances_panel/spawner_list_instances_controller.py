from __future__ import annotations

from typing import Optional, List, Dict, Any, Callable

from roguelike_editors.spawner.common import (
    ListPanelModel as SpawnerListInstancesModel,
    ListPanelView as SpawnerListInstancesView,
)
from .spawner_list_instances_events import SpawnerListInstancesEventHandler
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
        # Optional callback set by parent to react on selection change
        # Signature: (selected_index: Optional[int], selected_instance: Optional[dict]) -> None
        self.on_selection_changed: Optional[Callable[[Optional[int], Optional[Dict[str, Any]]], None]] = None
        # Optional callbacks to focus camera while holding LMB over coords segment
        # Signatures: on_start_hold_focus(x_px: float, y_px: float) and on_end_hold_focus()
        self.on_start_hold_focus: Optional[Callable[[float, float], None]] = None
        self.on_end_hold_focus: Optional[Callable[[], None]] = None

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        prev_idx = getattr(self.model, 'selected_index', None)
        handled = self.events.handle_event(self, event)
        try:
            cur_idx = getattr(self.model, 'selected_index', None)
            if cur_idx != prev_idx:
                if self.on_selection_changed is not None:
                    try:
                        self.on_selection_changed(cur_idx, self.get_selected_instance())
                    except Exception:
                        pass
        except Exception:
            pass
        return handled

    # --- Data ops ------------------------------------------------------------
    def refresh_from_disk(self) -> None:
        """Load spawners_instances.json and fill model.items and cache raw entries."""
        # Try to preserve selection by id across refreshes
        prev_selected_id = None
        try:
            idx_prev = getattr(self.model, 'selected_index', None)
            if idx_prev is not None and 0 <= idx_prev < len(self._instances):
                prev_selected_id = self._instances[idx_prev].get('id')
        except Exception:
            prev_selected_id = None

        data = load_instances_json()
        self._instances = data
        items: List[str] = []
        for inst in data:
            try:
                inst_id = inst.get('id')
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
                label_id = f"[{inst_id}] " if inst_id else ""
                # Show coords prefix first as requested: "@ zone (x,y) - [id]name"
                items.append(f"@ {zone} ({tile[0]},{tile[1]}) - {label_id}{tpl}{warn}")
            except Exception:
                items.append(str(inst))
        self.model.items = items
        # Restore selection by matching previous id if possible
        restored = False
        if prev_selected_id is not None:
            try:
                for i, inst in enumerate(self._instances):
                    if str(inst.get('id')) == str(prev_selected_id):
                        self.model.selected_index = i
                        restored = True
                        break
            except Exception:
                pass
        # Clamp selection if not restored and out of range
        if not restored and self.model.selected_index is not None and not (0 <= self.model.selected_index < len(items)):
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
        # Notify selection (to sync selected_instance pointer) if any
        try:
            if self.on_selection_changed is not None:
                self.on_selection_changed(self.model.selected_index, self.get_selected_instance())
        except Exception:
            pass

    def get_selected_instance(self) -> Optional[Dict[str, Any]]:
        idx = self.model.selected_index
        if idx is None:
            return None
        if 0 <= idx < len(self._instances):
            return self._instances[idx]
        return None


__all__ = ["SpawnerListInstancesController"]
