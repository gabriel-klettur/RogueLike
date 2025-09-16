from __future__ import annotations

from typing import Optional, Dict, Any, Tuple, List


class SelectionMixin:
    def set_instance(self, inst: Optional[Dict[str, Any]], *, index: Optional[int] = None) -> None:
        self.model.selected_instance = inst
        self.model.selected_index = index
        key: Optional[Tuple[str, str, Tuple[int, int]]] = None
        try:
            if inst is not None:
                # Track original id for robust persistence
                try:
                    self.model.original_id = str(inst.get('id')) if inst.get('id') is not None else None
                except (AttributeError, TypeError, ValueError):
                    self.model.original_id = None
                tpl = str(inst.get('template_id'))
                zone = str(inst.get('zone'))
                tile = tuple(inst.get('tile', [0, 0]))
                key = (tpl, zone, (int(tile[0]), int(tile[1])))
        except (AttributeError, TypeError, ValueError):
            key = None
        self.model.original_key = key
        self.model.visible = inst is not None
        # Reset UI state
        self.model.scroll_offset = 0
        self.model.hovered_index = None
        self.model.editing_key = None
        self.model.editing_row_index = None
        # Reset combo state and load options
        self.model.template_combo_open = False
        self.model.template_hovered_index = None
        self.model.template_scroll_offset = 0
        self._load_template_options()
        self._rows = self._flatten_instance()
        # Load visuals map and build rows
        visuals = {}
        try:
            if inst is not None and isinstance(inst.get('visuals'), dict):
                visuals = dict(inst.get('visuals') or {})
        except (AttributeError, TypeError, ValueError):
            visuals = {}
        self.model.visuals = visuals
        try:
            self._log_debug_rl("set_instance_loaded", f"[InstanceProps] set_instance: loaded visuals keys={list(visuals.keys()) if isinstance(visuals, dict) else visuals}", 1200)
        except (AttributeError, TypeError, ValueError):
            pass
        # Ensure buildings index is FRESH to avoid false sanitization of newly created instances
        try:
            self._building_index = None
        except AttributeError:
            pass
        self._ensure_buildings_index()
        self._ensure_building_templates()
        self._build_visuals_rows()
        # Clear any previous selection of a visual building when changing instance
        try:
            if hasattr(self, 'visuals') and getattr(self.visuals, 'model', None) is not None:
                self.visuals.model.selected_building_id = None
        except AttributeError:
            pass
        # Garbage collect invalid building instances in JSON (e.g., missing/invalid template_id) first
        try:
            self._gc_invalid_building_instances()
        except (AttributeError, TypeError, ValueError):
            pass
        # Then sanitize visuals mappings that point to missing instances
        try:
            self._sanitize_visuals_instances()
        except (AttributeError, TypeError, ValueError):
            # Best-effort; do not block UI if cleanup fails
            pass
