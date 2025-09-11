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
    load_spawners_json,
)
from roguelike_engine.config import config as _cfg
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config import BUILDINGS_INSTANCES_PATH, BUILDINGS_TEMPLATES_PATH
import os
import json
from .instance_properties_model import InstancePropertiesModel
from .instance_properties_view import InstancePropertiesView
from .instance_properties_events import InstancePropertiesEventHandler
from roguelike_editors.spawner.visuals_picker import VisualsPicker
from roguelike_engine.buildings.factory import build_from_config
import logging

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
        # Optional callback to notify editor about a saved instance with context
        # Signature: (inst: Dict[str, Any], changed_key: Optional[str]) -> None
        self.on_instance_saved: Optional[callable] = None
        # Track last edited dotted key path (e.g., "overrides.building_id")
        self._last_edit_key: Optional[str] = None
        # Cache of building instance id -> template_id (string)
        self._building_index: Dict[int, str] | None = None
        # Cache of valid building template ids
        self._building_template_ids: set[int] | None = None
        self.game = None
        # Visuals Picker orchestrator (lazy)
        self._visuals_picker: VisualsPicker | None = None
        # Editor-only visibility map for building instances (id -> visible)
        self._editor_visibility: Dict[int, bool] = {}
        # Toast defaults
        try:
            self._toast_ms = 1600
        except Exception:
            pass
        # Reduce repeated logs: keep a signature of last visuals we logged
        self._last_visuals_log_sig: tuple | None = None
        # Debounce window to avoid sanitizing right after creating/reusing/assigning
        self._sanitize_block_until_ms: int = 0
        # Logger
        self._log = logging.getLogger(__name__)
        try:
            if not self._log.handlers:
                _h = logging.StreamHandler()
                _h.setLevel(logging.DEBUG)
                _h.setFormatter(logging.Formatter('[%(levelname)s] %(name)s: %(message)s'))
                self._log.addHandler(_h)
            self._log.setLevel(logging.DEBUG)
            # Avoid duplicate logs due to root handlers
            self._log.propagate = False
        except Exception:
            pass

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
        except Exception:
            visuals = {}
        self.model.visuals = visuals
        try:
            self._log.debug(f"[InstanceProps] set_instance: loaded visuals keys={list(visuals.keys()) if isinstance(visuals, dict) else visuals}")
        except Exception:
            pass
        # Ensure buildings index is FRESH to avoid false sanitization of newly created instances
        try:
            self._building_index = None
        except Exception:
            pass
        self._ensure_buildings_index()
        self._ensure_building_templates()
        self._build_visuals_rows()
        # Garbage collect invalid building instances in JSON (e.g., missing/invalid template_id) first
        try:
            self._gc_invalid_building_instances()
        except Exception:
            pass
        # Then sanitize visuals mappings that point to missing instances
        try:
            self._sanitize_visuals_instances()
        except Exception:
            # Best-effort; do not block UI if cleanup fails
            pass

    def render(self, screen, *, anchor=None):
        if not self.model.visible:
            return None
        # Keep rows up to date
        self._rows = self._flatten_instance()
        # Rebuild visuals rows if visuals changed externally
        self._build_visuals_rows()
        # Sanitize mappings during render in case external GC removed instances
        try:
            # Ensure fresh building index to avoid false removals
            try:
                self._building_index = None
            except Exception:
                pass
            self._ensure_buildings_index()
            self._sanitize_visuals_instances()
        except Exception:
            pass
        return self.view.render(self, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)

    # --- Visuals Picker orchestration --------------------------------------
    def _on_visuals_picker_selected(self, state_key: str, template_id: int) -> None:
        """Callback invoked by the VisualsPicker when a template image is chosen."""
        try:
            self._log.info(f"[InstanceProps] Picker selected: state={state_key} tpl_id={template_id}")
            # Debounce sanitization for a short period while we create/persist/update indexes
            try:
                import pygame as _pg
                self._sanitize_block_until_ms = int((_pg.time.get_ticks() or 0) + 600)
            except Exception:
                self._sanitize_block_until_ms = 0
            self.set_visual_template_via_picker(state_key, int(template_id))
            # Toast feedback
            self._show_toast(f"Template aplicado: {int(template_id)} → {state_key}")
        except Exception:
            # Best effort; keep UI consistent
            pass
        # Close picker after applying
        try:
            self.model.visuals_picker_open = False
            self.model.visuals_picker_state = None
        except Exception:
            pass
        self._visuals_picker = None
        # Force refresh rows/index after applying to ensure UI reflects changes immediately
        try:
            self._building_index = None
            self._ensure_buildings_index()
            self._build_visuals_rows()
            # Reload from disk to ensure we keep in sync
            self._reload_selected_from_json()
            self._log.debug(f"[InstanceProps] After picker close, visuals_rows: {self.model.visuals_rows}")
        except Exception:
            pass

    def open_visuals_picker_for_state(self, state_key: str) -> None:
        """Open the visuals picker and bind it to the given visuals state key."""
        self.model.visuals_picker_state = str(state_key)
        self.model.visuals_picker_open = True
        # Create picker with callback bound to this state
        def _cb(tpl_id: int, _state=state_key):
            self._on_visuals_picker_selected(_state, int(tpl_id))
        self._visuals_picker = VisualsPicker(_cb)
        # Anchor below panel if available
        try:
            prec = getattr(self.view, 'panel_rect', None)
            if prec is not None and self._visuals_picker is not None:
                self._visuals_picker.set_anchors(left_x=prec.left, top_y=prec.bottom + 6, reserved_bottom_h=40)
        except Exception:
            pass
        try:
            self._log.debug(f"[InstanceProps] Opened VisualsPicker for state={state_key}")
        except Exception:
            pass

    def get_visuals_picker(self) -> VisualsPicker | None:
        return self._visuals_picker

    def handle_visuals_picker_event(self, event, camera) -> bool:
        if not getattr(self.model, 'visuals_picker_open', False) or self._visuals_picker is None:
            return False
        try:
            handled = bool(self._visuals_picker.handle_event(event, camera))
            # Debug a subset of events
            et = getattr(event, 'type', None)
            btn = getattr(event, 'button', None)
            if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                self._log.debug(f"[InstanceProps] Picker event: type={et} btn={btn}")
            return handled
        except Exception:
            return False

    def render_visuals_picker(self, screen, camera) -> None:
        if not getattr(self.model, 'visuals_picker_open', False) or self._visuals_picker is None:
            return
        try:
            self._visuals_picker.render(screen, camera)
        except Exception:
            pass

    # --- Internal helpers ----------------------------------------------------
    def _reload_selected_from_json(self) -> None:
        """Reload the current selected instance from spawners_instances.json by original_id.
        Keeps selection index updated and refreshes visuals and rows.
        """
        sid = getattr(self.model, 'original_id', None)
        if not sid:
            return
        try:
            data, idx, _ = find_instance_by_id(str(sid))
            if idx is None:
                return
            inst = data[idx]
            self.model.selected_instance = inst
            self.model.selected_index = idx
            visuals = {}
            try:
                if isinstance(inst.get('visuals'), dict):
                    visuals = dict(inst.get('visuals') or {})
            except Exception:
                visuals = {}
            self.model.visuals = visuals
            # Rebuild rows from fresh disk state
            self._ensure_buildings_index()
            self._build_visuals_rows()
            self._log.debug(f"[InstanceProps] _reload_selected_from_json: idx={idx} visuals={visuals}")
        except Exception:
            pass

    # --- Editor visibility helpers -----------------------------------------
    def _get_world(self):
        try:
            return getattr(getattr(self.game, 'ecs', None), 'ecs_world', None)
        except Exception:
            return None

    def _iter_building_entities(self):
        world = self._get_world()
        try:
            for ob in getattr(world, 'buildings', []) or []:
                yield ob
        except Exception:
            return

    def _find_building_entity_by_id(self, bid: int):
        for ob in self._iter_building_entities():
            try:
                if getattr(ob, 'id', None) == int(bid):
                    return ob
            except Exception:
                continue
        # Try to load on demand if not found
        try:
            self._ensure_building_loaded(int(bid))
            for ob in self._iter_building_entities():
                try:
                    if getattr(ob, 'id', None) == int(bid):
                        return ob
                except Exception:
                    continue
        except Exception:
            pass
        return None

    # JSON loaders for buildings data
    def _load_buildings_instances(self) -> list[dict]:
        try:
            with open(BUILDINGS_INSTANCES_PATH, 'r', encoding='utf-8') as f:
                data = json.load(f)
            return data if isinstance(data, list) else []
        except Exception:
            return []

    def _write_buildings_instances(self, data: list[dict]) -> None:
        try:
            with open(BUILDINGS_INSTANCES_PATH, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        except Exception:
            pass

    def _load_buildings_templates(self) -> list[dict]:
        try:
            with open(BUILDINGS_TEMPLATES_PATH, 'r', encoding='utf-8') as f:
                data = json.load(f)
            return data if isinstance(data, list) else []
        except Exception:
            return []

    def _get_template_image_path(self, template_id: int) -> str | None:
        for e in self._load_buildings_templates():
            try:
                if int(e.get('id')) == int(template_id):
                    # Prefer assets.idle, fallback to assets.image or image
                    assets = e.get('assets') if isinstance(e.get('assets'), dict) else {}
                    path = assets.get('idle') or assets.get('image') or e.get('image')
                    return str(path) if path else None
            except Exception:
                continue
        return None

    def _ensure_building_loaded(self, bid: int) -> None:
        """If building with id 'bid' is not present in world.buildings, load it from instances/templates and append it.
        This is editor-only best-effort to make a just-created/reused instance visible immediately."""
        world = self._get_world()
        if world is None:
            return
        # Already loaded?
        for ob in getattr(world, 'buildings', []) or []:
            try:
                if getattr(ob, 'id', None) == int(bid):
                    return
            except Exception:
                continue
        # Find instance entry
        inst_entry = None
        for e in self._load_buildings_instances():
            try:
                if int(e.get('id')) == int(bid):
                    inst_entry = e
                    break
            except Exception:
                continue
        if not inst_entry:
            return
        # Build config for factory
        cfg: dict[str, Any] = {}
        try:
            cfg['image_path'] = self._get_template_image_path(int(inst_entry.get('template_id')))
            cfg['rel_x'] = int(inst_entry.get('rel_x', 0) or 0)
            cfg['rel_y'] = int(inst_entry.get('rel_y', 0) or 0)
            # Zone
            if inst_entry.get('zone') is not None:
                cfg['zone'] = str(inst_entry.get('zone'))
            # Overrides
            ov = inst_entry.get('overrides') or {}
            if isinstance(ov, dict):
                if isinstance(ov.get('scale'), (list, tuple)) and len(ov.get('scale')) == 2:
                    cfg['scale'] = (int(ov['scale'][0]), int(ov['scale'][1]))
                if 'z_bottom' in ov:
                    cfg['z_bottom'] = int(ov['z_bottom'])
                if 'z_top' in ov:
                    cfg['z_top'] = int(ov['z_top'])
        except Exception:
            pass
        if not cfg.get('image_path'):
            return
        # Create Building and append to world
        try:
            cam = getattr(self.game, 'camera', None)
            b = build_from_config(cfg, camera=cam)
            # Attach persistent id and defaults
            try:
                setattr(b, 'id', int(bid))
            except Exception:
                pass
            # Optional flags used by editor/runtime
            try:
                setattr(b, 'visible', True)
                setattr(b, 'editor_hidden', False)
                setattr(b, 'runtime_hidden', False)
            except Exception:
                pass
            # Append to world list
            try:
                if not hasattr(world, 'buildings') or world.buildings is None:
                    setattr(world, 'buildings', [])
                world.buildings.append(b)
            except Exception:
                pass
            # Also append to game.entities.buildings if present (renderer may use it)
            try:
                ents = getattr(self.game, 'entities', None)
                if ents is not None and hasattr(ents, 'buildings') and ents.buildings is not None:
                    ents.buildings.append(b)
            except Exception:
                pass
        except Exception:
            # Best-effort: do nothing on failure
            pass

    def _set_building_visible(self, bid: int, visible: bool) -> None:
        self._editor_visibility[int(bid)] = bool(visible)
        ob = self._find_building_entity_by_id(int(bid))
        if ob is not None:
            # Try common visibility flags
            try:
                setattr(ob, 'visible', bool(visible))
            except Exception:
                pass
            try:
                setattr(ob, 'editor_hidden', not bool(visible))
            except Exception:
                pass

    def _tag_and_reveal_building(self, bid: int, state_key: str) -> None:
        """Attach spawner tags to the building entity and ensure it's visible for editing."""
        ob = self._find_building_entity_by_id(int(bid))
        if ob is None:
            return
        # Tag
        try:
            setattr(ob, '_is_spawner_visual', True)
        except Exception:
            pass
        try:
            inst = self.model.selected_instance or {}
            sid = str(inst.get('id')) if inst.get('id') is not None else None
            if sid is not None:
                setattr(ob, 'spawner_instance_id', sid)
                setattr(ob, 'spawn_id', sid)
        except Exception:
            pass
        try:
            setattr(ob, 'spawner_state_key', str(state_key))
        except Exception:
            pass
        # Link back to ECS entity if present (best-effort, similar to editor controller)
        try:
            world = self._get_world()
            comps = getattr(world, 'components', {}) if world else {}
            if world and 'SpawnerConfig' in comps:
                for eid in world.get_entities_with('SpawnerConfig'):
                    try:
                        cfg = comps['SpawnerConfig'][eid]
                        if getattr(ob, 'spawn_id', None) == str(getattr(cfg, 'template_id', '')):
                            setattr(ob, '_spawner_eid', eid)
                            setattr(ob, '_world_ref', world)
                            break
                    except Exception:
                        continue
        except Exception:
            pass
        # Reveal
        self._set_building_visible(int(bid), True)

    def is_visual_building_visible(self, state_key: str) -> bool:
        """Query current editor-visible state for the building instance wired to a given visuals state."""
        visuals = getattr(self.model, 'visuals', {}) or {}
        key_map = getattr(self.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        bid = visuals.get(json_key)
        if bid is None:
            return True
        try:
            return bool(self._editor_visibility.get(int(bid), True))
        except Exception:
            return True

    def toggle_visual_building_visibility(self, state_key: str) -> None:
        visuals = getattr(self.model, 'visuals', {}) or {}
        key_map = getattr(self.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        bid = visuals.get(json_key)
        if bid is None:
            return
        try:
            bid_int = int(bid)
        except Exception:
            return
        cur = bool(self._editor_visibility.get(bid_int, True))
        self._set_building_visible(bid_int, not cur)

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

    # Visuals helpers ---------------------------------------------------------
    def _ensure_buildings_index(self) -> None:
        if self._building_index is not None:
            return
        path = BUILDINGS_INSTANCES_PATH
        idx: Dict[int, str] = {}
        try:
            with open(path, 'r', encoding='utf-8') as f:
                arr = json.load(f)
            if isinstance(arr, list):
                for e in arr:
                    try:
                        bid = int(e.get('id'))
                        tpl = e.get('template_id')
                        # Store as string for display
                        idx[bid] = str(tpl)
                    except Exception:
                        continue
        except Exception:
            idx = {}
        self._building_index = idx

    def _ensure_building_templates(self) -> None:
        if self._building_template_ids is not None:
            return
        ids: set[int] = set()
        try:
            with open(BUILDINGS_TEMPLATES_PATH, 'r', encoding='utf-8') as f:
                arr = json.load(f)
            if isinstance(arr, list):
                for e in arr:
                    try:
                        tid = int(e.get('id'))
                        ids.add(tid)
                    except Exception:
                        continue
        except Exception:
            ids = set()
        self._building_template_ids = ids

    def _build_visuals_rows(self) -> None:
        visuals = getattr(self.model, 'visuals', {}) or {}
        # Log visuals only when they change (avoid per-frame spam)
        try:
            # Build a stable signature
            if isinstance(visuals, dict):
                sig = tuple(sorted((str(k), str(v)) for k, v in visuals.items()))
            else:
                sig = (str(visuals),)
            if sig != self._last_visuals_log_sig:
                self._last_visuals_log_sig = sig
                self._log.debug(f"[InstanceProps] visuals updated: {visuals}")
        except Exception:
            pass
        idx = self._building_index or {}
        rows: List[tuple[str, str, str]] = []

        # Canonical state order to display always (TitleCase)
        canonical_states: List[str] = [
            'AwaitTrigger',
            'SpawningWave',
            'WaitCooldown',
            'WaitClear',
            'WaitRestart',
            'Finished',
        ]

        def _to_snake(title: str) -> str:
            s = str(title or '')
            out = []
            for i, ch in enumerate(s):
                if ch.isupper() and i > 0:
                    out.append('_')
                out.append(ch.lower())
            return ''.join(out)

        def _candidates_for(canon: str) -> List[str]:
            snake = _to_snake(canon)
            return [
                str(canon),                  # TitleCase
                snake,                        # snake_case
                snake.replace('_', ''),       # condensed snake
                str(canon).lower(),           # lowercase title
            ]

        matched_keys: set[str] = set()
        # Map displayed canonical state -> actual key present in JSON (or None if missing)
        key_map: dict[str, str] = {}

        # 1) Emit rows for the canonical states, even if missing
        for canon in canonical_states:
            inst_val = None
            chosen_key = None
            for key in _candidates_for(canon):
                if key in visuals:
                    inst_val = visuals.get(key)
                    chosen_key = key
                    matched_keys.add(key)
                    break
            # Resolve instance id and template label
            inst_str = ''
            tpl_str = 'N/A'
            try:
                if inst_val is not None:
                    try:
                        inst_int = int(inst_val)
                    except Exception:
                        inst_int = None
                    inst_str = str(inst_val)
                    if inst_int is not None and inst_int in idx:
                        tpl_str = idx.get(inst_int, 'N/A')
            except Exception:
                pass
            # Record mapping for later editing/commit operations
            try:
                if chosen_key is not None:
                    key_map[str(canon)] = str(chosen_key)
            except Exception:
                pass
            rows.append((str(canon), inst_str, tpl_str))

        # 2) Append any extra custom states present in JSON that are not in canonical list
        try:
            for state, inst_id in visuals.items():
                if state in matched_keys:
                    continue
                # Skip if this state is equivalent to a canonical one (e.g., snake vs TitleCase)
                is_equiv = False
                for canon in canonical_states:
                    if state in _candidates_for(canon):
                        is_equiv = True
                        break
                if is_equiv:
                    continue
                # Compute template label
                try:
                    inst_int = int(inst_id)
                except Exception:
                    inst_int = None
                inst_str = str(inst_id)
                tpl_str = 'N/A'
                if inst_int is not None and inst_int in idx:
                    tpl_str = idx.get(inst_int, 'N/A')
                rows.append((str(state), inst_str, tpl_str))
        except Exception:
            pass

        self.model.visuals_rows = rows
        # Expose the display->JSON key mapping for event handlers/commits
        try:
            self.model.visuals_key_map = key_map
        except Exception:
            pass

    def _sanitize_visuals_instances(self) -> None:
        """Remove visuals entries whose instance id does not exist in buildings_instances.json.
        Rule: if a visuals state has no Template (would display as 'N/A'), then it must not have an Instance.
        Persists the spawner instance if any removals occur and rebuilds rows.
        """
        # Ensure we are checking against a fresh buildings index
        self._ensure_buildings_index()
        # Skip sanitization during the debounce window after user changes
        try:
            import pygame as _pg
            now = int(_pg.time.get_ticks() or 0)
        except Exception:
            now = 0
        if self._sanitize_block_until_ms and now < self._sanitize_block_until_ms:
            return
        visuals = dict(getattr(self.model, 'visuals', {}) or {})
        if not visuals:
            return
        idx = self._building_index or {}
        removed_any = False
        for k in list(visuals.keys()):
            v = visuals.get(k)
            if v is None:
                continue
            vid = None
            try:
                vid = int(v)
            except Exception:
                # Non-integer mapping is invalid
                vid = None
            if vid is None or vid not in idx:
                visuals.pop(k, None)
                removed_any = True
        if removed_any:
            # Apply and persist cleanup
            self.model.visuals = visuals
            try:
                if self.model.selected_instance is not None:
                    self.model.selected_instance['visuals'] = visuals
            except Exception:
                pass
            self._persist_instance()
            # Rebuild to refresh UI
            self._build_visuals_rows()

    def _gc_invalid_building_instances(self) -> None:
        """Remove entries from buildings_instances.json with invalid id or template_id.
        - Drops entries where 'id' is missing or non-integer
        - Drops entries where 'template_id' is missing, non-integer, or not present in templates
        Persists cleaned list and refreshes building index if any were removed.
        """
        # Ensure template ids
        self._ensure_building_templates()
        valid_tpls = self._building_template_ids or set()
        data = self._load_buildings_instances()
        if not data:
            return
        kept = []
        removed = False
        for e in data:
            try:
                eid = int(e.get('id'))
            except Exception:
                removed = True
                continue
            try:
                tid = int(e.get('template_id'))
            except Exception:
                removed = True
                continue
            if tid not in valid_tpls:
                removed = True
                continue
            kept.append(e)
        if removed:
            self._write_buildings_instances(kept)
            # Refresh index to reflect removals
            self._building_index = None
            self._ensure_buildings_index()

    def get_visuals_rows(self) -> List[tuple[str, str, str]]:
        return list(getattr(self.model, 'visuals_rows', []) or [])

    def _parse_int(self, s: str) -> Optional[int]:
        try:
            return int(float(str(s).strip()))
        except Exception:
            return None

    def _validate_template_text(self, text: str) -> tuple[bool, Optional[str], Optional[int]]:
        """Return (is_valid, error_msg, parsed_id). Empty text returns (True, None, None)."""
        t = (text or '').strip()
        if t == '':
            return True, None, None
        tpl_id = self._parse_int(t)
        if tpl_id is None:
            return False, "Debe ser un número de template", None
        self._ensure_building_templates()
        valid_ids = self._building_template_ids or set()
        if tpl_id not in valid_ids:
            return False, "Template no existe", tpl_id
        return True, None, tpl_id

    def get_visual_input_validation(self, state_key: str) -> tuple[bool, Optional[str]]:
        """Check current text being edited for a given state."""
        txt = (self.model.visuals_pending_templates or {}).get(state_key, '')
        if getattr(self.model, 'visuals_editing_state', None) == state_key and self._text_input is not None:
            try:
                txt = self._text_input.text
            except Exception:
                pass
        ok, msg, _ = self._validate_template_text(txt)
        return ok, msg

    # --- Visuals editing API -------------------------------------------------
    def set_game(self, game) -> None:
        self.game = game

    def begin_edit_visual(self, state_key: str) -> None:
        # Keep the displayed state key for UI matching
        self.model.visuals_editing_state = str(state_key)
        # Pre-fill pending template with current template text
        cur_tpl = 'N/A'
        try:
            rows = self.get_visuals_rows()
            for st, _iid, tpl in rows:
                if st == state_key:
                    cur_tpl = str(tpl)
                    break
        except Exception:
            pass
        # If template is N/A, start empty
        if cur_tpl.upper() == 'N/A':
            cur_tpl = ''
        self.model.visuals_pending_templates[state_key] = cur_tpl
        # Activate text input
        if self._text_input is None:
            font = pygame.font.SysFont(None, 18)
            self._text_input = TextInput(font)
        self._text_input.activate(cur_tpl, select_all=True)
        # Ensure OS text input is started for proper TEXTINPUT events
        try:
            import pygame as _pg
            _pg.key.start_text_input()
        except Exception:
            pass

    def cancel_edit_visual(self) -> None:
        self.model.visuals_editing_state = None
        try:
            import pygame as _pg
            _pg.key.stop_text_input()
        except Exception:
            pass

    def _load_buildings_instances(self) -> List[Dict[str, Any]]:
        path = BUILDINGS_INSTANCES_PATH
        try:
            with open(path, 'r', encoding='utf-8') as f:
                data = json.load(f)
                if isinstance(data, list):
                    return data
        except FileNotFoundError:
            return []
        except Exception:
            return []
        return []

    def _write_buildings_instances(self, data: List[Dict[str, Any]]) -> None:
        path = BUILDINGS_INSTANCES_PATH
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(data or [], f, ensure_ascii=False, indent=2)
        # Post-write GC to ensure consistency
        try:
            self._gc_invalid_building_instances()
        except Exception:
            pass

    def _count_instance_refs_in_visuals(self, inst_id: int) -> int:
        visuals = getattr(self.model, 'visuals', {}) or {}
        cnt = 0
        for k, v in visuals.items():
            try:
                if int(v) == inst_id:
                    cnt += 1
            except Exception:
                continue
        return cnt

    def _find_existing_visual_instance_by_template(self, template_id: int) -> Optional[int]:
        """Return an instance id already referenced by this spawner's visuals that uses the given template_id.
        This ensures we reuse the same building instance across states when they share the same template.
        """
        visuals = getattr(self.model, 'visuals', {}) or {}
        if not visuals:
            return None
        data = self._load_buildings_instances()
        # Build id -> template_id map for quick lookup
        tpl_by_id: dict[int, int] = {}
        for e in data:
            try:
                iid = int(e.get('id'))
                tid = int(e.get('template_id'))
                tpl_by_id[iid] = tid
            except Exception:
                continue
        for _, val in visuals.items():
            try:
                vid = int(val)
            except Exception:
                continue
            if vid in tpl_by_id and tpl_by_id[vid] == int(template_id):
                return vid
        return None

    def _clone_instance_with_new_template(self, source_id: int, new_template_id: int) -> Optional[int]:
        data = self._load_buildings_instances()
        src = None
        for e in data:
            try:
                if int(e.get('id')) == source_id:
                    src = e
                    break
            except Exception:
                continue
        if src is None:
            return None
        # Compute next id
        next_id = 1
        try:
            ids = [int(e.get('id')) for e in data if e.get('id') is not None]
            if ids:
                next_id = max(ids) + 1
        except Exception:
            pass
        clone = {
            'id': next_id,
            'template_id': int(new_template_id),
            'zone': src.get('zone'),
            'rel_x': src.get('rel_x'),
            'rel_y': src.get('rel_y'),
        }
        # Copy overrides if present
        if isinstance(src.get('overrides'), dict):
            clone['overrides'] = src['overrides']
        data.append(clone)
        self._write_buildings_instances(data)
        # Refresh index
        self._building_index = None
        self._ensure_buildings_index()
        return next_id

    def commit_visual_edit_if_finished(self) -> bool:
        display_state = getattr(self.model, 'visuals_editing_state', None)
        if not display_state:
            return False
        if self._text_input is None or self._text_input.active:
            return False
        # Read new template id
        new_txt = self._text_input.text if self._text_input else ''
        self.model.visuals_pending_templates[display_state] = new_txt
        ok, msg, new_tpl_id = self._validate_template_text(new_txt)
        # If invalid (not number or no existe) -> keep editing and show error
        if not ok and new_txt.strip() != '':
            # Re-activate input so user can correct
            try:
                self._text_input.activate(new_txt, select_all=False)
            except Exception:
                pass
            # Keep editing state
            return True

    def set_visual_template_via_picker(self, state_key: str, new_tpl_id: int) -> None:
        """Apply a template selection coming from the visuals picker overlay.

        Mirrors the logic used by inline text commit and the '+' flow, reusing existing
        helpers to update or clone building instances as needed, then rewires the visuals map
        and persists the spawner instance.
        """
        try:
            # Validate template id is known
            self._ensure_building_templates()
            if (self._building_template_ids or set()) and new_tpl_id not in (self._building_template_ids or set()):
                return
        except Exception:
            pass
        # Current visuals row info
        rows = self.get_visuals_rows()
        cur_inst_int: Optional[int] = None
        for st, inst_str, _tpl in rows:
            if st == state_key:
                try:
                    cur_inst_int = int(float(str(inst_str))) if str(inst_str).strip() != '' and str(inst_str).upper() != 'N/A' else None
                except Exception:
                    cur_inst_int = None
                break
        self._log.debug(f"[InstanceProps] set_visual_template_via_picker: state={state_key} tpl={new_tpl_id} cur_inst={cur_inst_int}")
        # If there was an instance id and user provided a valid template id
        if cur_inst_int is not None and new_tpl_id is not None:
            # Check if current instance already has the desired template -> nothing to do
            desired = int(new_tpl_id)
            if (self._building_index or {}).get(cur_inst_int, None) == str(desired):
                return
            # If this instance is used only by this state: update its template in-place
            if self._count_instance_refs_in_visuals(cur_inst_int) <= 1:
                # Load instances json and patch
                data = self._load_buildings_instances()
                changed = False
                for e in data:
                    try:
                        if int(e.get('id')) == cur_inst_int:
                            e['template_id'] = int(desired)
                            changed = True
                            break
                    except Exception:
                        continue
                if changed:
                    self._write_buildings_instances(data)
                    # refresh index/rows
                    self._building_index = None
                    self._ensure_buildings_index()
                    self._build_visuals_rows()
                    self._log.info(f"[InstanceProps] Updated instance {cur_inst_int} -> template {desired}")
                    try:
                        # Log current row for state
                        for r in (self.model.visuals_rows or []):
                            if r[0] == state_key:
                                self._log.debug(f"[InstanceProps] Row after update: {r}")
                                break
                    except Exception:
                        pass
            else:
                # Shared by multiple states: clone and rewire only this state
                new_id = self._clone_instance_with_new_template(cur_inst_int, int(desired))
                if new_id is not None:
                    visuals = getattr(self.model, 'visuals', {}) or {}
                    key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                    json_key = key_map.get(state_key, state_key)
                    visuals[json_key] = new_id
                    self.model.visuals = visuals
                    try:
                        if self.model.selected_instance is not None:
                            self.model.selected_instance['visuals'] = visuals
                    except Exception:
                        pass
                    self._persist_instance()
                    # Rebuild views/indexes
                    self._ensure_buildings_index()
                    self._build_visuals_rows()
                    self._log.info(f"[InstanceProps] Cloned instance {cur_inst_int} -> new_id {new_id} tpl {desired} for state {state_key}")
                    try:
                        for r in (self.model.visuals_rows or []):
                            if r[0] == state_key:
                                self._log.debug(f"[InstanceProps] Row after clone: {r}")
                                break
                    except Exception:
                        pass
            return
        # If there was no instance id and user provided a valid template id: reuse or create
        if cur_inst_int is None and new_tpl_id is not None:
            desired = int(new_tpl_id)
            reuse_id = self._find_existing_visual_instance_by_template(desired)
            if reuse_id is not None:
                visuals = getattr(self.model, 'visuals', {}) or {}
                key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                json_key = key_map.get(state_key, state_key)
                visuals[json_key] = reuse_id
                self.model.visuals = visuals
                try:
                    if self.model.selected_instance is not None:
                        self.model.selected_instance['visuals'] = visuals
                except Exception:
                    pass
                self._persist_instance()
                self._ensure_buildings_index()
                self._build_visuals_rows()
                self._log.info(f"[InstanceProps] Reused existing instance {reuse_id} for state {state_key} tpl {desired}")
                try:
                    for r in (self.model.visuals_rows or []):
                        if r[0] == state_key:
                            self._log.debug(f"[InstanceProps] Row after reuse: {r}")
                            break
                except Exception:
                    pass
            else:
                # Reuse the '+' flow helper to create instance at camera center
                try:
                    # Prime pending input so add_building_instance_for_visual uses it
                    self.model.visuals_pending_templates[state_key] = str(desired)
                except Exception:
                    pass
                # Do not reveal/spawn when selecting via picker
                new_id = self.add_building_instance_for_visual(state_key, reveal=False)
                self._log.info(f"[InstanceProps] Created new instance {new_id} for state {state_key} tpl {desired}")
                try:
                    for r in (self.model.visuals_rows or []):
                        if r[0] == state_key:
                            self._log.debug(f"[InstanceProps] Row after create: {r}")
                            break
                except Exception:
                    pass
        # also toast here as a safety (if picker flow ends here)
        try:
            self._show_toast(f"Template aplicado: {int(new_tpl_id)} → {state_key}")
        except Exception:
            pass
        # Done
        return

    def add_building_instance_for_visual(self, state_key: str, reveal: bool = True) -> Optional[int]:
        # Need a template id: prefer current text input if editing this state
        txt = (self.model.visuals_pending_templates or {}).get(state_key, '')
        if getattr(self.model, 'visuals_editing_state', None) == state_key and self._text_input is not None:
            try:
                txt = self._text_input.text
            except Exception:
                pass
        ok, msg, tpl_id = self._validate_template_text(txt)
        if tpl_id is None or not ok:
            return None
        # Prefer reuse: check if another state already uses an instance with this template
        reuse_id = self._find_existing_visual_instance_by_template(int(tpl_id))
        if reuse_id is not None:
            visuals = getattr(self.model, 'visuals', {}) or {}
            key_map = getattr(self.model, 'visuals_key_map', {}) or {}
            json_key = key_map.get(state_key, state_key)
            visuals[json_key] = reuse_id
            self.model.visuals = visuals
            try:
                if self.model.selected_instance is not None:
                    self.model.selected_instance['visuals'] = visuals
            except Exception:
                pass
            try:
                self._log.debug(f"[InstanceProps] add_building_instance_for_visual: set visuals[{json_key}]={reuse_id}")
            except Exception:
                pass
            self._persist_instance()
            # Refresh indexes/rows
            self._building_index = None
            self._ensure_buildings_index()
            self._build_visuals_rows()
            # Exit edit mode
            self.model.visuals_editing_state = None
            # Ensure it is visible and tagged for editing (optional)
            if reveal:
                try:
                    self._tag_and_reveal_building(int(reuse_id), state_key)
                except Exception:
                    pass
            return reuse_id
        # Load buildings instances and compute next id
        data = self._load_buildings_instances()
        next_id = 1
        try:
            ids = [int(e.get('id')) for e in data if e.get('id') is not None]
            if ids:
                next_id = max(ids) + 1
        except Exception:
            pass
        # Determine zone and rel_x/rel_y from camera center relative to zone
        zone = None
        try:
            zone = str((self.model.selected_instance or {}).get('zone'))
        except Exception:
            zone = None
        if not zone:
            zone = 'lobby'
        cx = cy = 0
        try:
            cam = getattr(self.game, 'camera', None)
            if cam is not None:
                zoom = getattr(cam, 'zoom', 1.0) or 1.0
                cx = int(getattr(cam, 'offset_x', 0.0) + (cam.screen_width / (2 * zoom)))
                cy = int(getattr(cam, 'offset_y', 0.0) + (cam.screen_height / (2 * zoom)))
        except Exception:
            pass
        # Convert zone offsets to pixels (offsets likely in tiles)
        off_x, off_y = 0, 0
        try:
            oz = global_map_settings.zone_offsets.get(zone, (0, 0))
            off_x = int(oz[0] * TILE_SIZE)
            off_y = int(oz[1] * TILE_SIZE)
        except Exception:
            pass
        rel_x = int(cx - off_x)
        rel_y = int(cy - off_y)
        entry = {
            'id': next_id,
            'template_id': tpl_id,
            'zone': zone,
            'rel_x': int(rel_x),
            'rel_y': int(rel_y),
        }
        data.append(entry)
        self._write_buildings_instances(data)
        # Update visuals mapping and persist spawner instance
        visuals = getattr(self.model, 'visuals', {}) or {}
        # Map displayed canonical to actual JSON key if present; otherwise use the displayed key
        key_map = getattr(self.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        visuals[json_key] = next_id
        try:
            self._log.debug(f"[InstanceProps] add_building_instance_for_visual: set visuals[{json_key}]={next_id}")
        except Exception:
            pass
        self.model.visuals = visuals
        try:
            if self.model.selected_instance is not None:
                self.model.selected_instance['visuals'] = visuals
        except Exception:
            pass
        try:
            self._log.debug(f"[InstanceProps] add_building_instance_for_visual: model.visuals now={self.model.visuals}")
        except Exception:
            pass
        self._persist_instance()
        # Reload from disk to be 100% sure it persisted and update UI model
        try:
            self._reload_selected_from_json()
        except Exception:
            pass
        # Refresh indexes/rows
        self._building_index = None
        self._ensure_buildings_index()
        self._build_visuals_rows()
        # Exit edit mode
        self.model.visuals_editing_state = None
        # Ensure it is visible and tagged for editing (optional)
        if reveal:
            try:
                self._tag_and_reveal_building(int(next_id), state_key)
            except Exception:
                pass
        return next_id

    # --- Template combobox helpers ------------------------------------------
    def _load_template_options(self) -> None:
        try:
            data = load_spawners_json()
            ids = []
            for sp in data:
                try:
                    sid = str(sp.get('id'))
                    if sid:
                        ids.append(sid)
                except Exception:
                    continue
            ids = sorted(set(ids))
            self.model.template_options = ids
        except Exception:
            self.model.template_options = []

    def get_template_options(self) -> List[str]:
        if not getattr(self.model, 'template_options', None):
            self._load_template_options()
        return list(self.model.template_options)

    def get_current_template_index(self) -> Optional[int]:
        opts = self.get_template_options()
        inst = self.model.selected_instance or {}
        cur = None
        try:
            cur = str(inst.get('template_id'))
        except Exception:
            cur = None
        if cur is None:
            return None
        try:
            return opts.index(cur)
        except ValueError:
            return None

    def select_template_by_index(self, idx: int) -> None:
        opts = self.get_template_options()
        if not (0 <= idx < len(opts)):
            return
        self.set_template_id(opts[idx])

    def set_template_id(self, new_id: str) -> None:
        inst = self.model.selected_instance
        if inst is None:
            return
        try:
            inst['template_id'] = str(new_id)
        except Exception:
            inst['template_id'] = new_id  # type: ignore
        # Persist and refresh
        self._persist_instance()
        self._rows = self._flatten_instance()

    def begin_edit_row(self, row_index: int) -> None:
        rows = self.get_rows()
        if not (0 <= row_index < len(rows)):
            return
        key, value_str = rows[row_index]
        self.model.editing_key = key
        self.model.editing_row_index = row_index
        # Initialize last edit key with the row key being edited
        try:
            self._last_edit_key = str(key)
        except Exception:
            self._last_edit_key = key
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
            # Persist to spawners_instances.json
            # Remember the changed key path for callbacks
            try:
                self._last_edit_key = str(key_path)
            except Exception:
                self._last_edit_key = key_path
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
        try:
            self._log.debug(f"[InstanceProps] _persist_instance: about to persist id={inst.get('id')} visuals={inst.get('visuals')}")
        except Exception:
            pass
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
        try:
            self._log.debug(f"[InstanceProps] _persist_instance: wrote instance id={inst.get('id')} with visuals keys={list((inst.get('visuals') or {}).keys()) if isinstance(inst.get('visuals'), dict) else inst.get('visuals')}")
        except Exception:
            pass
        # Update original ids/keys for subsequent edits
        self.model.original_id = str(inst.get('id')) if inst and inst.get('id') is not None else None
        self.model.original_key = cur_key
        # Notify UI to refresh instances list if requested
        try:
            if self.on_persist is not None:
                self.on_persist()
        except Exception:
            pass
        # Notify editor about saved instance with context (changed key)
        try:
            if self.on_instance_saved is not None:
                self.on_instance_saved(inst, getattr(self, '_last_edit_key', None))
        except Exception:
            pass
        # Clear last edit key after notifying
        self._last_edit_key = None


__all__ = ["InstancePropertiesController"]
