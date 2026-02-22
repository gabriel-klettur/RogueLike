from __future__ import annotations

from typing import Optional, List, Dict, Any, Callable

from roguelike_editors.spawner.common import (
    ListPanelModel as SpawnerListInstancesModel,
)
from .spawner_list_instances_view import SpawnerListInstancesView
from .spawner_list_instances_events import SpawnerListInstancesEventHandler
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services.persistence import (
    load_instances_json,
    write_instances_json,
    find_instance_in_json,
    load_spawners_json,
    zone_for_global_tile,
)
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
from roguelike_game.ecs.systems.spawner.placement.loaders import load_waves
from roguelike_game.ecs.systems.spawner.placement.config_resolver import resolve_config
from roguelike_game.ecs.systems.spawner.placement.visuals import auto_repair_state_visuals


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
            setattr(self.model, 'panel_width', 840)
        except Exception:
            pass
        # Raw instances cache corresponding to rows in model.items
        self._instances: List[Dict[str, Any]] = []
        # Map from visual row index -> instance index (None for headers)
        self._row_to_instance_idx: Dict[int, int] = {}
        self._hidden_ids: set[str] = set()
        # Grouping toggle: when True, list is grouped under zone headers
        self.group_by_zone: bool = False
        # Optional callback set by parent to react on selection change
        # Signature: (selected_index: Optional[int], selected_instance: Optional[dict]) -> None
        self.on_selection_changed: Optional[Callable[[Optional[int], Optional[Dict[str, Any]]], None]] = None
        # Optional callbacks to focus camera while holding LMB over coords segment
        # Signatures: on_start_hold_focus(x_px: float, y_px: float) and on_end_hold_focus()
        self.on_start_hold_focus: Optional[Callable[[float, float], None]] = None
        self.on_end_hold_focus: Optional[Callable[[], None]] = None

    def select_by_tpl_zone_tile(self, tpl_id: str, zone: str, local_tile: tuple[int, int]) -> None:
        try:
            self.refresh_from_disk()
        except Exception:
            pass
        try:
            idx = None
            for i, e in enumerate(self._instances or []):
                try:
                    if str(e.get('template_id')) == str(tpl_id) and str(e.get('zone')) == str(zone):
                        t = e.get('tile') or [0, 0]
                        tx = int(t[0]) if isinstance(t, (list, tuple)) and len(t) >= 1 else 0
                        ty = int(t[1]) if isinstance(t, (list, tuple)) and len(t) >= 2 else 0
                        if (tx, ty) == (int(local_tile[0]), int(local_tile[1])):
                            idx = i
                            break
                except Exception:
                    continue
            if idx is None:
                return
            row = None
            for r, ii in (self._row_to_instance_idx or {}).items():
                if ii == idx:
                    row = r
                    break
            if row is None:
                return
            self.model.selected_index = int(row)
            # Ensure row is visible by adjusting scroll
            try:
                visible_rows = int(getattr(self.model, 'visible_rows', 11) or 11)
                start = int(getattr(self.model, 'scroll_offset', 0) or 0)
                if not (start <= int(row) < start + visible_rows):
                    new_off = max(0, int(row) - visible_rows // 2)
                    max_off = max(0, len(self.model.items) - visible_rows)
                    self.model.scroll_offset = min(new_off, max_off)
            except Exception:
                pass
            # Optional blink feedback
            try:
                import pygame  # type: ignore
                now = pygame.time.get_ticks()
                setattr(self.model, '_blink_row_index', int(row))
                setattr(self.model, '_blink_end_ticks', int(now + 450))
            except Exception:
                pass
        except Exception:
            pass

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
        try:
            hid = {str(x) for x in getattr(self, '_hidden_ids', set())}
        except Exception:
            hid = set()
        data = [inst for inst in data if str(inst.get('id')) not in hid]
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

    def hide_instance_by_id(self, inst_id: Optional[str]) -> None:
        try:
            if inst_id is None:
                return
            self._hidden_ids.add(str(inst_id))
        except Exception:
            return
        self.refresh_from_disk()

    def duplicate_instance_at(self, row_index: int) -> None:
        try:
            idx = self.instance_index_for_row(row_index)
        except Exception:
            idx = None
        if idx is None:
            return
        try:
            orig = dict(self._instances[idx])
        except Exception:
            return
        try:
            tpl = str(orig.get('template_id'))
            zone = str(orig.get('zone'))
            tile = orig.get('tile') or [0, 0]
            ox, oy = int(tile[0]), int(tile[1])
        except Exception:
            return
        try:
            existing = {
                (str(e.get('template_id')), str(e.get('zone')), int((e.get('tile') or [0, 0])[0]), int((e.get('tile') or [0, 0])[1]))
                for e in (load_instances_json() or [])
            }
        except Exception:
            existing = set()
        candidates = [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (2, 0), (0, 2), (-2, 0), (0, -2)]
        nx, ny = ox, oy
        for dx, dy in candidates:
            cx, cy = ox + dx, oy + dy
            if (tpl, zone, cx, cy) not in existing:
                nx, ny = cx, cy
                break
        new_entry: Dict[str, Any] = {
            'template_id': tpl,
            'zone': zone,
            'tile': [int(nx), int(ny)],
        }
        try:
            ov = orig.get('overrides')
            if isinstance(ov, dict) and ov:
                new_entry['overrides'] = dict(ov)
        except Exception:
            pass
        try:
            data = load_instances_json()
            data.append(new_entry)
            write_instances_json(data)
        except Exception:
            return
        try:
            data2 = load_instances_json()
            _data2, idx_found, _ = find_instance_in_json(tpl, zone, (int(nx), int(ny)))
            inst_norm = data2[idx_found] if idx_found is not None else new_entry
        except Exception:
            inst_norm = new_entry
        try:
            editor = getattr(self, 'editor', None)
            world = getattr(getattr(getattr(editor, 'game', None), 'ecs', None), 'ecs_world', None)
            if world is not None:
                tpls = load_spawners_json()
                tpl_def = None
                for t in (tpls or []):
                    try:
                        if str(t.get('id')) == tpl:
                            tpl_def = t
                            break
                    except Exception:
                        continue
                if tpl_def is not None:
                    waves = load_waves()
                    cfg = resolve_config(tpl_def, inst_norm, waves)
                    eid = world.create_entity()
                    world.components.setdefault('SpawnerConfig', {})[eid] = cfg
                    world.components.setdefault('SpawnerState', {})[eid] = SpawnerState()
                    try:
                        auto_repair_state_visuals(world, eid, cfg, inst_norm)
                    except Exception:
                        pass
        except Exception:
            pass
        try:
            self.refresh_from_disk()
            new_id = str(inst_norm.get('id')) if inst_norm.get('id') is not None else None
            if new_id is not None:
                i_idx = None
                for i, e in enumerate(self._instances):
                    try:
                        if str(e.get('id')) == new_id:
                            i_idx = i
                            break
                    except Exception:
                        continue
                if i_idx is not None:
                    for row, ii in self._row_to_instance_idx.items():
                        if ii == i_idx:
                            self.model.selected_index = row
                            break
        except Exception:
            pass

    def prepare_delete_at(self, row_index: int) -> None:
        try:
            idx = self.instance_index_for_row(row_index)
        except Exception:
            idx = None
        if idx is None:
            return
        try:
            inst = self._instances[idx]
            tpl = str(inst.get('template_id'))
            zone = str(inst.get('zone'))
            tile = inst.get('tile') or [0, 0]
            lt = (int(tile[0]), int(tile[1]))
        except Exception:
            return
        eid = None
        try:
            editor = getattr(self, 'editor', None)
            world = getattr(getattr(getattr(editor, 'game', None), 'ecs', None), 'ecs_world', None)
            if world is not None:
                comps = getattr(world, 'components', {})
                cfgs = comps.get('SpawnerConfig', {})
                off = global_map_settings.zone_offsets.get(zone, (0, 0))
                gx = int(off[0]) + lt[0]
                gy = int(off[1]) + lt[1]
                for cand_eid, cfg in list(cfgs.items()):
                    try:
                        if str(getattr(cfg, 'template_id', '')) != tpl:
                            continue
                        tx, ty = getattr(cfg, 'anchor_tile', (None, None))
                        if int(tx) == gx and int(ty) == gy:
                            eid = cand_eid
                            break
                    except Exception:
                        continue
        except Exception:
            eid = None
        try:
            editor = getattr(self, 'editor', None)
            if editor is not None:
                payload = {'eid': eid, 'template_id': tpl, 'zone': zone, 'local_tile': (lt[0], lt[1])}
                editor.model.pending_delete_confirm = payload
                world = getattr(getattr(getattr(editor, 'game', None), 'ecs', None), 'ecs_world', None)
                if world is not None and hasattr(world, 'state'):
                    try:
                        setattr(world.state, 'spawner_remove_candidate_eid', eid)
                        setattr(world.state, 'spawner_input_suppressed', True)
                    except Exception:
                        pass
        except Exception:
            pass


__all__ = ["SpawnerListInstancesController"]
