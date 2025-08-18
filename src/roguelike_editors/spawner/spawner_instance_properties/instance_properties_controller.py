from __future__ import annotations

from typing import Optional, List, Dict, Any, Tuple
import ast

import pygame
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_ui.widgets.text_input import TextInput
from roguelike_editors.spawner.services.persistence import (
    load_instances_json,
    write_instances_json,
    find_instance_in_json,
    find_instance_by_id,
    generate_instance_id,
)
from .instance_properties_model import InstancePropertiesModel
from .instance_properties_view import InstancePropertiesView
from .instance_properties_events import InstancePropertiesEventHandler


class InstancePropertiesController:
    def __init__(self,
                 model: Optional[InstancePropertiesModel] = None,
                 view: Optional[InstancePropertiesView] = None) -> None:
        self.model = model or InstancePropertiesModel()
        self.view = view or InstancePropertiesView()
        self.events = InstancePropertiesEventHandler()
        # UI helpers
        self._dbl = DoubleClickDetector(interval_ms=450)
        self._text_input: Optional[TextInput] = None
        # Cache flattened rows (key, value_str)
        self._rows: List[Tuple[str, str]] = []
        # Optional callback for editor to refresh Instances list after persistence
        # Signature: () -> None
        self.on_persist: Optional[callable] = None

    # --- API -----------------------------------------------------------------
    def set_instance(self, inst: Optional[Dict[str, Any]], *, index: Optional[int] = None) -> None:
        self.model.selected_instance = inst
        self.model.selected_index = index
        key = None
        try:
            if inst is not None:
                # Track original id for robust persistence
                try:
                    self.model.original_id = str(inst.get('id')) if inst.get('id') is not None else None
                except Exception:
                    self.model.original_id = None
                tpl = str(inst.get('template_id'))
                zone = str(inst.get('zone'))
                tile = tuple(inst.get('tile', [0, 0]))
                key = (tpl, zone, (int(tile[0]), int(tile[1])))
        except Exception:
            key = None
        self.model.original_key = key
        self.model.visible = inst is not None
        # Reset UI state
        self.model.scroll_offset = 0
        self.model.hovered_index = None
        self.model.editing_key = None
        self.model.editing_row_index = None
        self._rows = self._flatten_instance()

    def render(self, screen, *, anchor=None):
        if not self.model.visible:
            return None
        # Keep rows up to date
        self._rows = self._flatten_instance()
        return self.view.render(self, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)

    # --- Rows & Editing ------------------------------------------------------
    def _flatten_instance(self) -> List[Tuple[str, str]]:
        data = self.model.selected_instance or {}
        # Present a stable order: id, template_id, zone, tile, overrides.*
        flat: List[Tuple[str, str]] = []
        try:
            flat.append(("id", str(data.get('id'))))
        except Exception:
            pass
        try:
            # Simple fields
            flat.append(("template_id", str(data.get('template_id'))))
        except Exception:
            pass
        try:
            flat.append(("zone", str(data.get('zone'))))
        except Exception:
            pass
        try:
            tile = data.get('tile', [0, 0])
            flat.append(("tile.0", str(tile[0] if isinstance(tile, (list, tuple)) and len(tile) > 0 else 0)))
            flat.append(("tile.1", str(tile[1] if isinstance(tile, (list, tuple)) and len(tile) > 1 else 0)))
        except Exception:
            pass
        # Overrides tree
        try:
            ov = data.get('overrides')
            if isinstance(ov, dict):
                for k, v in self.view._flatten(ov, prefix="overrides"):  # reuse view flattener
                    flat.append((k, v))
        except Exception:
            pass
        return flat

    def get_rows(self) -> List[Tuple[str, str]]:
        return list(self._rows)

    def begin_edit_row(self, row_index: int) -> None:
        rows = self.get_rows()
        if not (0 <= row_index < len(rows)):
            return
        key, value_str = rows[row_index]
        self.model.editing_key = key
        self.model.editing_row_index = row_index
        if self._text_input is None:
            font = pygame.font.SysFont(None, 18)
            self._text_input = TextInput(font)
        self._text_input.activate(value_str, select_all=True)

    def is_editing(self) -> bool:
        return self.model.editing_key is not None and self._text_input is not None and self._text_input.active

    def get_text_input(self) -> Optional[TextInput]:
        return self._text_input

    def commit_edit_if_finished(self) -> bool:
        if self.model.editing_key and self._text_input and not self._text_input.active:
            key_path = self.model.editing_key
            new_text = self._text_input.text
            # Parse new value and apply to selected_instance
            new_value = self._parse_value(new_text, key_path)
            self._apply_edit(key_path, new_value)
            # Persist to instances.json
            self._persist_instance()
            # Clear editing state and refresh rows
            self.model.editing_key = None
            self.model.editing_row_index = None
            self._rows = self._flatten_instance()
            return True
        return False

    # --- Utils ---------------------------------------------------------------
    def _parse_value(self, text: str, key_path: str):
        t = (text or "").strip()
        low = t.lower()
        if low == 'true':
            return True
        if low == 'false':
            return False
        if low in ('null', 'none'):
            return None
        # number
        try:
            if t.startswith('0') and t != '0' and not t.startswith('0.'):
                raise ValueError()
            if '.' in t:
                return float(t)
            return int(t)
        except Exception:
            pass
        # JSON/list/dict
        if (t.startswith('[') and t.endswith(']')) or (t.startswith('{') and t.endswith('}')):
            try:
                import json
                return json.loads(t)
            except Exception:
                try:
                    return ast.literal_eval(t)
                except Exception:
                    pass
        return text

    def _apply_edit(self, key_path: str, value) -> None:
        inst = self.model.selected_instance
        if inst is None:
            return
        # Special-case tile.0 and tile.1 to force list length and int
        if key_path.startswith('tile.'):
            try:
                idx = int(key_path.split('.')[-1])
            except Exception:
                idx = None
            if idx is not None:
                tile = inst.get('tile')
                if not isinstance(tile, list):
                    tile = [0, 0]
                while len(tile) <= idx:
                    tile.append(0)
                try:
                    tile[idx] = int(value)
                except Exception:
                    try:
                        tile[idx] = int(float(value))
                    except Exception:
                        pass
                inst['tile'] = tile
                return
        # Normal dotted path set (supports overrides.* tree)
        self._set_by_path(inst, key_path, value)

    def _set_by_path(self, root: Dict[str, Any] | None, path: str, value) -> None:
        if root is None:
            return
        parts = path.split('.') if path else []
        cur: Any = root
        for i, part in enumerate(parts):
            is_last = (i == len(parts) - 1)
            idx: Optional[int] = None
            try:
                idx = int(part)
            except Exception:
                idx = None
            if idx is not None and isinstance(cur, list):
                if is_last:
                    cur[idx] = value
                else:
                    # If next is out of bounds, extend with dicts
                    while len(cur) <= idx:
                        cur.append({})
                    cur = cur[idx]
            else:
                if is_last:
                    if isinstance(cur, dict):
                        cur[part] = value
                else:
                    if isinstance(cur, dict):
                        nxt = cur.get(part)
                        if nxt is None:
                            nxt = {} if not parts[i+1].isdigit() else []
                            cur[part] = nxt
                        cur = nxt

    def _persist_instance(self) -> None:
        inst = self.model.selected_instance
        if inst is None:
            return
        # Reload data fresh
        data = load_instances_json()
        # Compute identities
        cur_id = None
        try:
            cur_id = str(inst.get('id')) if inst.get('id') is not None else None
        except Exception:
            cur_id = None
        cur_key = None
        try:
            tpl = str(inst.get('template_id'))
            zone = str(inst.get('zone'))
            tile = tuple(inst.get('tile', [0, 0]))
            cur_key = (tpl, zone, (int(tile[0]), int(tile[1])))
        except Exception:
            cur_key = None

        # Determine target index prioritizing original id, then index+key, then key search
        target_idx: Optional[int] = None
        # 1) If we have an original id, replace that exact entry
        if self.model.original_id:
            data_by_id, idx_by_id, _ = find_instance_by_id(self.model.original_id)
            if data_by_id is not None:
                data = data_by_id
            if idx_by_id is not None:
                target_idx = idx_by_id
        # 2) If not found yet, try validating stored index with original key
        if target_idx is None:
            idx = self.model.selected_index
            if idx is not None and 0 <= idx < len(data):
                ok = True
                try:
                    if self.model.original_key is not None:
                        e = data[idx]
                        ek = (str(e.get('template_id')), str(e.get('zone')), (int(e.get('tile', [0, 0])[0]), int(e.get('tile', [0, 0])[1])))
                        ok = (ek == self.model.original_key)
                except Exception:
                    ok = False
                if ok:
                    target_idx = idx
        # 3) Try original key lookup
        if target_idx is None and self.model.original_key is not None:
            tpl0, zone0, local0 = self.model.original_key
            data2, found_idx, _ = find_instance_in_json(tpl0, zone0, local0)
            if data2 is not None:
                data = data2
            if found_idx is not None:
                target_idx = found_idx
        # 4) As last resort, try current identity search
        if target_idx is None and cur_key is not None:
            for i, e in enumerate(data):
                try:
                    ek = (str(e.get('template_id')), str(e.get('zone')), (int(e.get('tile', [0, 0])[0]), int(e.get('tile', [0, 0])[1])))
                    if ek == cur_key:
                        target_idx = i
                        break
                except Exception:
                    continue

        # Ensure a unique 'id' for the instance (handle rename conflicts)
        existing_ids = {str(e.get('id')) for e in data if e.get('id')}
        if target_idx is not None:
            # Exclude current target from conflict set
            try:
                existing_ids.discard(str(data[target_idx].get('id')))
            except Exception:
                pass
        desired_id = cur_id
        if not desired_id or desired_id in existing_ids:
            inst['id'] = generate_instance_id(inst, existing_ids)
        # Persist replace/append
        if target_idx is not None:
            data[target_idx] = inst
        else:
            data.append(inst)
        write_instances_json(data)
        # Update original ids/keys for subsequent edits
        self.model.original_id = str(inst.get('id')) if inst and inst.get('id') is not None else None
        self.model.original_key = cur_key
        # Notify UI to refresh instances list if requested
        try:
            if self.on_persist is not None:
                self.on_persist()
        except Exception:
            pass


__all__ = ["InstancePropertiesController"]
