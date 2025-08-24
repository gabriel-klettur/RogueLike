from __future__ import annotations

from typing import Optional, Callable
import ast

from .spawners_manager_model import SpawnersManagerModel
from .spawners_manager_view import SpawnersManagerView
from .spawners_manager_events import SpawnersManagerEventHandler
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_ui.widgets.text_input import TextInput
from roguelike_editors.spawner.services.persistence import save_spawner_template, rename_spawner_template_id
import pygame


class SpawnersManagerController:
    def __init__(self,
                 model: Optional[SpawnersManagerModel] = None,
                 view: Optional[SpawnersManagerView] = None) -> None:
        self.model = model or SpawnersManagerModel()
        self.view = view or SpawnersManagerView()
        self.events = SpawnersManagerEventHandler()
        # Optional callback set by parent controller to react on id renames
        # Signature: (old_id: str, new_id: str) -> None
        self.on_template_renamed: Optional[Callable[[str, str], None]] = None
        # Optional callback invoked after saving non-id edits to the template
        # Signature: (updated_template: dict) -> None
        self.on_template_saved: Optional[Callable[[dict], None]] = None
        # UI helpers
        self._dbl = DoubleClickDetector(interval_ms=450)
        self._text_input: Optional[TextInput] = None
        # Cache of flattened rows (key, value_str)
        self._rows: list[tuple[str, str]] = []
        # Tooltip dictionary (basic defaults; can be extended)
        self.tooltips = {
            'id': 'Unique template identifier',
            'spawner_type': 'Visual or invisible spawner entity type',
            'trigger.type': 'Trigger mode: proximity, manual, on_enter, etc.',
            'trigger.radius': 'Proximity radius in tiles',
            'trigger.auto_start': 'If true, trigger is active without interaction',
            'policy.mode': 'Spawn policy: periodic, single, wave, etc.',
            'policy.cooldown_s': 'Cooldown between activations (seconds)',
            'policy.max_active': '0 for unlimited; otherwise max entities',
            'policy.persistent': 'Persist spawned entities across loads',
        }

    # --- API -----------------------------------------------------------------
    def set_template(self, tpl: Optional[dict]) -> None:
        self.model.selected_template = tpl
        self.model.visible = tpl is not None
        # Reset scroll when selection changes
        self.model.scroll_offset = 0
        self.model.hovered_index = None
        self.model.editing_key = None
        self.model.editing_row_index = None
        self._rows = self._flatten_template()

    def render(self, screen, *, anchor=None):
        if not self.model.visible:
            return None
        # Ensure rows cache is up to date
        self._rows = self._flatten_template()
        return self.view.render(self, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)

    # --- Utilities -----------------------------------------------------------
    def _flatten_template(self) -> list[tuple[str, str]]:
        data = self.model.selected_template or {}
        # Use view's flattener for consistency
        try:
            return self.view._flatten(data)
        except Exception:
            # Fallback simple flatten
            rows: list[tuple[str, str]] = []
            def rec(obj, prefix=""):
                if isinstance(obj, dict):
                    for k, v in obj.items():
                        p = f"{prefix}.{k}" if prefix else str(k)
                        rec(v, p)
                else:
                    rows.append((prefix, str(obj)))
            rec(data)
            return rows

    def get_rows(self) -> list[tuple[str, str]]:
        return list(self._rows)

    def get_tooltip_lines(self, key: str) -> list[str]:
        # Exact match or by suffix
        if key in self.tooltips:
            return [self.tooltips[key]]
        # Try to match end of path like '*.radius'
        for k, v in self.tooltips.items():
            if key.endswith(k):
                return [v]
        # Fallback
        return [key]

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
        # If we have a text input and it's no longer active, commit
        if self.model.editing_key and self._text_input and not self._text_input.active:
            key_path = self.model.editing_key
            new_text = self._text_input.text
            # Special-case renaming template id to keep JSONs consistent
            if key_path == 'id':
                old_id = None
                try:
                    old_id = str((self.model.selected_template or {}).get('id'))
                except Exception:
                    old_id = None
                new_id = (new_text or '').strip()
                # Only attempt if non-empty and changed
                if new_id and old_id and new_id != old_id:
                    updated = None
                    try:
                        updated = rename_spawner_template_id(old_id, new_id)
                    except Exception:
                        updated = None
                    if updated is not None:
                        # Update current selection to the renamed template
                        self.model.selected_template = updated
                        # Notify parent so it can refresh list/selection
                        if self.on_template_renamed and old_id is not None:
                            try:
                                self.on_template_renamed(old_id, new_id)
                            except Exception:
                                pass
                    else:
                        # Conflict or failure: keep old id in model
                        if self.model.selected_template is not None:
                            self.model.selected_template['id'] = old_id
                # If empty or unchanged, do nothing (revert to old visually)
            else:
                new_value = self._parse_value(new_text, key_path)
                # Apply into selected_template
                self._set_by_path(self.model.selected_template, key_path, new_value)
                # Persist
                try:
                    save_spawner_template(self.model.selected_template)  # type: ignore[arg-type]
                except Exception:
                    pass
                # Notify listeners that a template was saved (e.g., to refresh ECS spawners)
                try:
                    if self.on_template_saved and self.model.selected_template is not None:
                        self.on_template_saved(self.model.selected_template)
                except Exception:
                    pass
            # Clear
            self.model.editing_key = None
            self.model.editing_row_index = None
            # Recompute rows
            self._rows = self._flatten_template()
            return True
        return False

    def _parse_value(self, text: str, key_path: str):
        # Try bool/null/number/json, else plain string
        t = text.strip()
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
        # json array/object
        if (t.startswith('[') and t.endswith(']')) or (t.startswith('{') and t.endswith('}')):
            try:
                import json
                return json.loads(t)
            except Exception:
                # Try Python-literal fallback (handles single quotes)
                try:
                    return ast.literal_eval(t)
                except Exception:
                    pass
        return text

    def _set_by_path(self, root: dict | None, path: str, value) -> None:
        if root is None:
            return
        parts = path.split('.') if path else []
        cur = root
        for i, part in enumerate(parts):
            is_last = (i == len(parts) - 1)
            # Try to interpret numeric index for lists
            idx = None
            try:
                idx = int(part)
            except Exception:
                idx = None
            if idx is not None and isinstance(cur, list):
                if is_last:
                    cur[idx] = value
                else:
                    cur = cur[idx]
            else:
                if is_last:
                    if isinstance(cur, dict):
                        cur[part] = value
                else:
                    if isinstance(cur, dict):
                        cur = cur.get(part)


__all__ = ["SpawnersManagerController"]
