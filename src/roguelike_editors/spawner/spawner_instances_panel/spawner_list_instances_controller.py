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
        # Narrower panel width for Instances list as requested (default is 720 in ListPanelView)
        try:
            setattr(self.model, 'panel_width', 420)
        except Exception:
            pass
        # Raw instances cache corresponding to rows in model.items
        self._instances: List[Dict[str, Any]] = []
        # Map from visual row index -> instance index (None for headers)
        self._row_to_instance_idx: Dict[int, int] = {}
        # Grouping toggle: when True, list is grouped under zone headers
        self.group_by_zone: bool = False
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
        # Try to preserve selection by id across refreshes (row-aware)
        prev_selected_id = None
        try:
            cur_inst = self.get_selected_instance()
            if cur_inst is not None:
                prev_selected_id = cur_inst.get('id')
        except Exception:
            prev_selected_id = None

        data = load_instances_json()
        self._instances = data
        # Rebuild items and row mapping (optionally grouped)
        items: List[str] = []
        row_to_idx: Dict[int, int] = {}

        def _make_item_label(inst: Dict[str, Any]) -> str:
            try:
                inst_id = inst.get('id')
                tpl = inst.get('template_id', '?')
                zone = inst.get('zone', '?')
                tile = inst.get('tile', [0, 0])
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
                return f"@ {zone} ({tile[0]},{tile[1]}) - {label_id}{tpl}{warn}"
            except Exception:
                return str(inst)

        if not self.group_by_zone:
            for i, inst in enumerate(data):
                items.append(_make_item_label(inst))
                row_to_idx[len(items) - 1] = i
        else:
            # Build groups: zone -> list of (instance_index)
            groups: Dict[str, List[int]] = {}
            for i, inst in enumerate(data):
                zone = str(inst.get('zone', '?'))
                groups.setdefault(zone, []).append(i)
            # Deterministic order: sort by zone name
            for zone_name in sorted(groups.keys(), key=lambda s: (s is None, str(s))):
                indices = groups[zone_name]
                # Header row (non-selectable). Ensure it does not start with '@ ' so no coords hitbox.
                items.append(f"Zona: {zone_name}  ({len(indices)})")
                # No mapping entry for header row
                for i_idx in indices:
                    items.append(_make_item_label(self._instances[i_idx]))
                    row_to_idx[len(items) - 1] = i_idx
        self._row_to_instance_idx = row_to_idx
        # Update model title hinting grouping state if supported
        try:
            base_title = "Spawner Instances"
            self.model.title = f"{base_title}  [Group by zone: {'ON' if self.group_by_zone else 'OFF'}]"
        except Exception:
            pass
        self.model.items = items
        # Restore selection by matching previous id if possible
        restored = False
        if prev_selected_id is not None:
            try:
                # Find instance index by id
                inst_idx = None
                for i, inst in enumerate(self._instances):
                    if str(inst.get('id')) == str(prev_selected_id):
                        inst_idx = i
                        break
                if inst_idx is not None:
                    # Find first row that maps to this instance
                    for row, ii in self._row_to_instance_idx.items():
                        if ii == inst_idx:
                            self.model.selected_index = row
                            restored = True
                            break
            except Exception:
                pass
        # Clamp selection if not restored and out of range
        if not restored and self.model.selected_index is not None and not (0 <= self.model.selected_index < len(items)):
            self.model.selected_index = None
        # If nothing is selected and there are items, auto-select the first one to populate Properties
        if self.model.selected_index is None and len(items) > 0:
            # Prefer first selectable row (skip headers)
            first_row = None
            for row in range(len(items)):
                if row in self._row_to_instance_idx:
                    first_row = row
                    break
            if first_row is not None:
                self.model.selected_index = first_row
                restored = True
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
        idx = getattr(self.model, 'selected_index', None)
        if idx is None:
            return None
        # Map visual row -> instance index if available
        try:
            if idx in self._row_to_instance_idx:
                inst_idx = self._row_to_instance_idx[idx]
                if 0 <= inst_idx < len(self._instances):
                    return self._instances[inst_idx]
            else:
                # Fallback to legacy behavior when mapping not built
                if 0 <= idx < len(self._instances):
                    return self._instances[idx]
        except Exception:
            pass
        return None

    # --- Helpers -------------------------------------------------------------
    def is_row_instance(self, row: Optional[int]) -> bool:
        if row is None:
            return False
        return row in self._row_to_instance_idx

    def instance_index_for_row(self, row: Optional[int]) -> Optional[int]:
        if row is None:
            return None
        return self._row_to_instance_idx.get(int(row))

    def toggle_group_by_zone(self) -> None:
        try:
            self.group_by_zone = not bool(self.group_by_zone)
        except Exception:
            self.group_by_zone = False
        self.refresh_from_disk()


__all__ = ["SpawnerListInstancesController"]
