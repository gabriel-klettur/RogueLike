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
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from .instance_properties_model import InstancePropertiesModel
from .instance_properties_view import InstancePropertiesView
from .instance_properties_events import InstancePropertiesEventHandler
from .visuals.visuals_controller import VisualsController
from .visuals.visuals_picker import VisualsPicker
from .services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
    load_buildings_templates as svc_load_buildings_templates,
    get_template_image_path as svc_get_template_image_path,
)
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
        # Cache flattened rows (key, value_str)
        self._rows: List[Tuple[str, str]] = []
        # visuals (Visuals table MVC)
        self.visuals = VisualsController(self)
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
        # Editor-only visibility moved to visuals.model
        # Toast defaults
        try:
            self._toast_ms = 1600
        except AttributeError:
            pass
        # Strict cleanup policy: when clearing Visuals, also remove untagged
        # building instances that are tied to this spawner via root-level
        # identifiers (spawn_id/spawner_instance_id). Default ON as per user
        # request ("modo estricto").
        try:
            self.strict_visuals_cleanup = True
        except AttributeError:
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
        except (AttributeError, ValueError):
            pass

    # --- API -----------------------------------------------------------------
    def set_game(self, game) -> None:
        """Provide access to game (camera/world) for visuals operations."""
        try:
            self.game = game
        except AttributeError:
            self.game = None
        # No need to pass to visuals: it dereferences parent.game dynamically

    def set_instance(self, inst: Optional[Dict[str, Any]], *, index: Optional[int] = None) -> None:
        self.model.selected_instance = inst
        self.model.selected_index = index
        key = None
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
            self._log.debug(f"[InstanceProps] set_instance: loaded visuals keys={list(visuals.keys()) if isinstance(visuals, dict) else visuals}")
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
            except AttributeError:
                pass
            self._ensure_buildings_index()
            self._sanitize_visuals_instances()
        except (AttributeError, TypeError, ValueError):
            pass
        # While holding on a visuals row, keep camera centered on its building
        try:
            vmodel = getattr(self.visuals, 'model', None)
            if vmodel is not None and getattr(vmodel, 'hold_active', False):
                j = getattr(vmodel, 'hold_row_index', None)
                vis_rows = self.get_visuals_rows()
                if j is not None and 0 <= int(j) < len(vis_rows):
                    st = str(vis_rows[int(j)][0])
                    self.visuals.center_camera_on_state(st)
        except (AttributeError, TypeError, ValueError):
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
            except (ImportError, AttributeError, TypeError, ValueError):
                self._sanitize_block_until_ms = 0
            self.set_visual_template_via_picker(state_key, int(template_id))
            # Toast feedback
            self._show_toast(f"Template aplicado: {int(template_id)} → {state_key}")
        except (AttributeError, TypeError, ValueError):
            # Best effort; keep UI consistent
            pass
        # Close picker after applying
        try:
            self.model.visuals_picker_open = False
            self.model.visuals_picker_state = None
        except AttributeError:
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
        except (AttributeError, TypeError, ValueError, OSError):
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
        except AttributeError:
            pass
        try:
            self._log.debug(f"[InstanceProps] Opened VisualsPicker for state={state_key}")
        except (AttributeError, TypeError, ValueError):
            pass

    def get_visuals_picker(self) -> VisualsPicker | None:
        return self._visuals_picker

    def handle_visuals_picker_event(self, event, camera) -> bool:
        if not getattr(self.model, 'visuals_picker_open', False) or self._visuals_picker is None:
            return False
        try:
            handled = self._visuals_picker.handle_event(event)
            # Debug log only for mouse clicks and keydown
            et = getattr(event, 'type', None)
            btn = getattr(event, 'button', None)
            if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                self._log.debug(f"[InstanceProps] Picker event: type={et} btn={btn}")
            return handled
        except AttributeError:
            return False

    def render_visuals_picker(self, screen, camera) -> None:
        if not getattr(self.model, 'visuals_picker_open', False) or self._visuals_picker is None:
            # Ensure UI reloads from persisted disk state
            try:
                self._reload_selected_from_json()
            except (AttributeError, OSError, ValueError, TypeError):
                pass
            return
        try:
            self._visuals_picker.render(screen, camera)
        except AttributeError:
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
            except (AttributeError, TypeError, ValueError):
                visuals = {}
            self.model.visuals = visuals
            # Rebuild rows from fresh disk state
            self._ensure_buildings_index()
            self._build_visuals_rows()
            self._log.debug(f"[InstanceProps] _reload_selected_from_json: idx={idx} visuals={visuals}")
        except (AttributeError, TypeError, ValueError, OSError):
            pass

    # --- Editor visibility helpers -----------------------------------------
    def _get_world(self):
        # Delegated to visuals
        return getattr(self.visuals, '_get_world')()

    def _iter_building_entities(self):
        # Delegated to visuals
        yield from self.visuals._iter_building_entities()

    def _find_building_entity_by_id(self, bid: int):
        return self.visuals._find_building_entity_by_id(int(bid))

    # Obsolete building JSON/template helpers were removed. The visuals
    # now owns world/buildings operations, and this controller keeps only the
    # robust loaders used for persistence further below.

    def _ensure_building_loaded(self, bid: int) -> None:
        # Delegated to visuals
        self.visuals._ensure_building_loaded(int(bid))

    def _set_building_visible(self, bid: int, visible: bool) -> None:
        # Delegated to visuals
        self.visuals._set_building_visible(int(bid), bool(visible))

    def _tag_and_reveal_building(self, bid: int, state_key: str) -> None:
        # Delegated to visuals
        self.visuals.tag_and_reveal_building(int(bid), str(state_key))

    def is_visual_building_visible(self, state_key: str) -> bool:
        return self.visuals.is_building_visible_for_state(str(state_key))

    def toggle_visual_building_visibility(self, state_key: str) -> None:
        self.visuals.toggle_building_visibility_for_state(str(state_key))

    def _remove_building_entity_by_id(self, bid: int) -> bool:
        """Hard-remove a Building object with the given id from the running world/editor.
        Returns True if any object was removed.
        """
        removed_any = False
        # Remove from ECS world list
        try:
            world = self._get_world()
            if world is not None and hasattr(world, 'buildings') and isinstance(world.buildings, list):
                arr = world.buildings
                for i in range(len(arr) - 1, -1, -1):
                    try:
                        if getattr(arr[i], 'id', None) == int(bid):
                            arr.pop(i)
                            removed_any = True
                    except (AttributeError, TypeError, ValueError):
                        continue
        except AttributeError:
            pass
        # Remove from editor/game registry
        try:
            ents = getattr(self, 'game', None)
            ents = getattr(ents, 'entities', None)
            if ents is not None and hasattr(ents, 'buildings') and isinstance(ents.buildings, list):
                arr2 = ents.buildings
                for i in range(len(arr2) - 1, -1, -1):
                    try:
                        if getattr(arr2[i], 'id', None) == int(bid):
                            arr2.pop(i)
                            removed_any = True
                    except (AttributeError, TypeError, ValueError):
                        continue
        except AttributeError:
            pass
        # Best-effort: clear any cached visibility flags
        try:
            if int(bid) in self.visuals.model.editor_visibility:
                self.visuals.model.editor_visibility.pop(int(bid), None)
        except (AttributeError, TypeError, ValueError):
            pass
        return removed_any

    # --- Rows & Editing ------------------------------------------------------
    def _flatten_instance(self) -> List[Tuple[str, str]]:
        data = self.model.selected_instance or {}
        # Present a stable order: id, template_id, zone, tile, overrides.*
        flat: List[Tuple[str, str]] = []
        try:
            flat.append(("id", str(data.get('id'))))
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            # Simple fields
            flat.append(("template_id", str(data.get('template_id'))))
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            flat.append(("zone", str(data.get('zone'))))
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            tile = data.get('tile', [0, 0])
            flat.append(("tile.0", str(tile[0] if isinstance(tile, (list, tuple)) and len(tile) > 0 else 0)))
            flat.append(("tile.1", str(tile[1] if isinstance(tile, (list, tuple)) and len(tile) > 1 else 0)))
        except (AttributeError, TypeError, ValueError):
            pass
        # Overrides tree
        try:
            ov = data.get('overrides')
            if isinstance(ov, dict):
                for k, v in self.view._flatten(ov, prefix="overrides"):  # reuse view flattener
                    flat.append((k, v))
        except (AttributeError, TypeError, ValueError):
            pass
        return flat

    def get_rows(self) -> List[Tuple[str, str]]:
        return list(self._rows)

    # Visuals helpers ---------------------------------------------------------
    def _ensure_buildings_index(self) -> None:
        if self._building_index is not None:
            return
        try:
            arr = svc_load_buildings_instances()
            idx = {}
            if isinstance(arr, list):
                for e in arr:
                    try:
                        bid = int(e.get('id'))
                        tid = str(e.get('template_id'))
                        idx[bid] = tid
                    except (AttributeError, TypeError, ValueError):
                        continue
        except (OSError, ValueError, TypeError, AttributeError):
            idx = {}
        self._building_index = idx

    def _ensure_building_templates(self) -> None:
        if self._building_template_ids is not None:
            return
        ids: set[int] = set()
        try:
            arr = svc_load_buildings_templates()
            if isinstance(arr, list):
                for e in arr:
                    try:
                        tid = int(e.get('id'))
                        ids.add(tid)
                    except (AttributeError, TypeError, ValueError):
                        continue
        except (OSError, ValueError, TypeError, AttributeError):
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
        except (TypeError, ValueError):
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
                    # Support new format: dict with instance/template
                    if isinstance(inst_val, dict):
                        try:
                            inst_int = int(inst_val.get('instance_id') or inst_val.get('id') or inst_val.get('building_instance_id'))
                        except (AttributeError, TypeError, ValueError):
                            inst_int = None
                        tpl_from_val = inst_val.get('template_id') if isinstance(inst_val, dict) else None
                        if inst_int is not None:
                            inst_str = str(inst_int)
                        else:
                            inst_str = ''
                        if tpl_from_val is not None:
                            tpl_str = str(tpl_from_val)
                        elif inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
                    else:
                        try:
                            inst_int = int(inst_val)
                        except (TypeError, ValueError):
                            inst_int = None
                        inst_str = str(inst_val)
                        if inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
            except (AttributeError, TypeError, ValueError):
                pass
            # Record mapping for later editing/commit operations
            try:
                if chosen_key is not None:
                    key_map[str(canon)] = str(chosen_key)
            except (AttributeError, TypeError, ValueError):
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
                inst_int = None
                inst_str = ''
                tpl_str = 'N/A'
                try:
                    if isinstance(inst_id, dict):
                        try:
                            inst_int = int(inst_id.get('instance_id') or inst_id.get('id') or inst_id.get('building_instance_id'))
                        except (AttributeError, TypeError, ValueError):
                            inst_int = None
                        if inst_int is not None:
                            inst_str = str(inst_int)
                        tpl_from_val = inst_id.get('template_id')
                        if tpl_from_val is not None:
                            tpl_str = str(tpl_from_val)
                        elif inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
                    else:
                        try:
                            inst_int = int(inst_id)
                        except (TypeError, ValueError):
                            inst_int = None
                        inst_str = str(inst_id)
                        if inst_int is not None and inst_int in idx:
                            tpl_str = idx.get(inst_int, 'N/A')
                except (AttributeError, TypeError, ValueError):
                    pass
                rows.append((str(state), inst_str, tpl_str))
        except (AttributeError, TypeError, ValueError):
            pass

        self.model.visuals_rows = rows
        # Expose the display->JSON key mapping for event handlers/commits
        try:
            self.model.visuals_key_map = key_map
        except (AttributeError, TypeError, ValueError):
            pass

    def _sanitize_visuals_instances(self) -> None:
        """Remove visuals entries whose instance id does not exist in buildings_instances.json.
        Rule: if a visuals state has no Template (would display as 'N/A'), then it must not have an Instance.
        Persists the spawner instance if any removals occur and rebuilds rows.
        """
        # Ensure we are checking against a fresh buildings index
        self._ensure_buildings_index()
        # Ensure we have valid building template ids to allow template-only mappings
        self._ensure_building_templates()
        # Prefer disk truth: do not recreate mappings that were already removed on disk
        disk_visuals_keys: set[str] = set()
        try:
            sid = getattr(self.model, 'original_id', None)
            if sid:
                data, idx, _ = find_instance_by_id(str(sid))
                if idx is not None:
                    vis_disk = data[idx].get('visuals')
                    if isinstance(vis_disk, dict):
                        disk_visuals_keys = {str(k) for k in vis_disk.keys()}
        except (AttributeError, TypeError, ValueError, OSError):
            disk_visuals_keys = set()
        # Skip sanitization during debounce window
        try:
            import pygame as _pg
            now = int(_pg.time.get_ticks() or 0)
        except (ImportError, AttributeError, TypeError, ValueError):
            now = 0
        if self._sanitize_block_until_ms and now < self._sanitize_block_until_ms:
            try:
                self._log.debug(f"[InstanceProps] sanitize_visuals: SKIP (debounce) now={now} until={self._sanitize_block_until_ms}")
            except (AttributeError, TypeError, ValueError):
                pass
            return
        visuals = dict(getattr(self.model, 'visuals', {}) or {})
        if not visuals:
            return
        idx = self._building_index or {}
        # Helper: consider a building present if it's in JSON index OR currently spawned in world
        def _building_exists(bid: int) -> bool:
            try:
                if int(bid) in idx:
                    return True
            except (AttributeError, TypeError, ValueError):
                pass
            try:
                if self._find_building_entity_by_id(int(bid)) is not None:
                    return True
            except (AttributeError, TypeError, ValueError):
                pass
            return False
        valid_tpls = self._building_template_ids or set()
        removed_any = False
        repaired_any = False
        for k in list(visuals.keys()):
            v = visuals.get(k)
            if v is None:
                continue
            # If the mapping is no longer present on disk for this instance, drop it without repair
            try:
                if disk_visuals_keys and (str(k) not in disk_visuals_keys):
                    try:
                        self._log.info(f"[InstanceProps] sanitize_visuals: dropping state='{k}' (absent on disk)")
                    except (AttributeError, TypeError, ValueError):
                        pass
                    visuals.pop(k, None)
                    removed_any = True
                    continue
            except (AttributeError, TypeError, ValueError):
                pass
            vid = None
            vtpl = None
            try:
                if isinstance(v, dict):
                    vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                    try:
                        vtpl = int(v.get('template_id')) if v.get('template_id') is not None else None
                    except (AttributeError, TypeError, ValueError):
                        vtpl = None
                else:
                    vid = int(v)
            except (AttributeError, TypeError, ValueError):
                # Non-integer/invalid mapping is invalid
                vid = None
            # Keep or repair mapping
            repaired = False
            if vid is not None and _building_exists(int(vid)):
                keep = True
            elif isinstance(v, dict) and vtpl is not None and vtpl in valid_tpls:
                # Auto-repair by creating/reusing an instance for this template
                try:
                    try:
                        if not hasattr(self.model, 'visuals_pending_templates') or self.model.visuals_pending_templates is None:
                            self.model.visuals_pending_templates = {}
                    except (AttributeError, TypeError):
                        pass
                    self.model.visuals_pending_templates[str(k)] = str(int(vtpl))
                    new_id = self.add_building_instance_for_visual(str(k), reveal=False)
                    if new_id is not None:
                        visuals[str(k)] = {'instance_id': int(new_id), 'template_id': int(vtpl)}
                        repaired = True
                        repaired_any = True
                        try:
                            self._log.info(f"[InstanceProps] sanitize_visuals: repaired state='{k}' -> instance_id={new_id} tpl={vtpl}")
                        except (AttributeError, TypeError, ValueError):
                            pass
                    else:
                        repaired = False
                except (AttributeError, TypeError, ValueError, OSError):
                    repaired = False
                keep = repaired
            else:
                keep = False
            if not keep and not repaired:
                try:
                    reason = 'invalid' if vid is None else 'missing in buildings and no valid template_id'
                    self._log.warning(f"[InstanceProps] sanitize_visuals: removing state='{k}' reason={reason} value={v}")
                except (AttributeError, TypeError, ValueError):
                    pass
                visuals.pop(k, None)
                removed_any = True
        if removed_any or repaired_any:
            # Apply and persist cleanup
            self.model.visuals = visuals
            try:
                if self.model.selected_instance is not None:
                    self.model.selected_instance['visuals'] = visuals
            except AttributeError:
                pass
            self._persist_instance()
            # Rebuild to refresh UI
            self._build_visuals_rows()
            try:
                self._log.info(f"[InstanceProps] sanitize_visuals: persisted cleanup/repairs keys={list(visuals.keys())}")
            except (AttributeError, TypeError, ValueError):
                pass


    def _gc_invalid_building_instances(self) -> None:
        """Remove entries from buildings_instances.json with invalid id or template_id.
        - Drops entries where 'id' is missing or non-integer
        - Drops entries where 'template_id' is missing, non-integer, or not present in templates
        - Deduplicates entries by (zone, rel_x, rel_y, template_id) keeping the best candidate
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
        # First filter invalids
        for e in data:
            try:
                eid = int(e.get('id'))
                tid = int(e.get('template_id'))
            except (AttributeError, TypeError, ValueError):
                removed = True
                continue
            if tid not in valid_tpls:
                removed = True
                continue
            kept.append(e)
        # Then deduplicate by (zone,rel_x,rel_y,template_id)
        try:
            seen: dict[str, dict] = {}
            def _key(e: dict) -> str:
                try:
                    zone = str(e.get('zone') or 'lobby')
                    rx = int(e.get('rel_x') or 0)
                    ry = int(e.get('rel_y') or 0)
                    tid = int(e.get('template_id') or -1)
                    return f"{zone}|{rx}|{ry}|{tid}"
                except (AttributeError, TypeError, ValueError):
                    return str(id(e))
            def _score(e: dict) -> tuple:
                ov = e.get('overrides') if isinstance(e, dict) else None
                is_spawn_vis = 1 if (isinstance(ov, dict) and bool(ov.get('_is_spawner_visual'))) else 0
                # Prefer entries tied to current selected spawner if available
                tied_to_me = 0
                try:
                    inst = self.model.selected_instance or {}
                    sid = str(inst.get('id')) if inst.get('id') is not None else None
                    if sid and (str(e.get('spawner_instance_id')) == sid or str((ov or {}).get('spawner_instance_id')) == sid):
                        tied_to_me = 1
                except (AttributeError, TypeError, ValueError):
                    tied_to_me = 0
                try:
                    neg_id = -int(e.get('id') or 0)
                except (AttributeError, TypeError, ValueError):
                    neg_id = 0
                return (tied_to_me, is_spawn_vis, neg_id)
            for e in kept:
                k = _key(e)
                cur = seen.get(k)
                if cur is None or _score(e) > _score(cur):
                    seen[k] = e
            deduped = list(seen.values())
            if len(deduped) != len(kept):
                kept = deduped
                removed = True
        except (AttributeError, TypeError, ValueError):
            pass
        if removed:
            try:
                self._log.warning(f"[InstanceProps] GC/Dedup buildings_instances: before={len(data)} after={len(kept)} removed={len(data)-len(kept)}")
            except (AttributeError, TypeError, ValueError):
                pass
            self._write_buildings_instances(kept)
            # Refresh index to reflect removals
            self._building_index = None
            self._ensure_buildings_index()

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
        if getattr(self.model, 'visuals_editing_state', None) == state_key:
            vti = getattr(self.visuals.model, 'text_input', None)
            if vti is not None:
                try:
                    txt = vti.text
                except AttributeError:
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
        except (AttributeError, TypeError, ValueError):
            pass
        # If template is N/A, start empty
        if cur_tpl.upper() == 'N/A':
            cur_tpl = ''
        self.model.visuals_pending_templates[state_key] = cur_tpl
        # Activate visuals's dedicated text input
        vti = getattr(self.visuals.model, 'text_input', None)
        if vti is None:
            font = pygame.font.SysFont(None, 18)
            vti = TextInput(font)
            self.visuals.model.text_input = vti
        vti.activate(cur_tpl, select_all=True)
        # Ensure OS text input is started for proper TEXTINPUT events
        try:
            import pygame as _pg
            _pg.key.start_text_input()
        except (ImportError, AttributeError, pygame.error):
            pass

    def cancel_edit_visual(self) -> None:
        self.model.visuals_editing_state = None
        try:
            import pygame as _pg
            _pg.key.stop_text_input()
        except (ImportError, AttributeError, pygame.error):
            pass

    def _load_buildings_instances(self) -> List[Dict[str, Any]]:
        return svc_load_buildings_instances()

    def _write_buildings_instances(self, data: List[Dict[str, Any]]) -> None:
        svc_write_buildings_instances(data)
        # Post-write GC to ensure consistency
        try:
            self._gc_invalid_building_instances()
        except (AttributeError, TypeError, ValueError):
            pass

    def _count_instance_refs_in_visuals(self, inst_id: int) -> int:
        visuals = getattr(self.model, 'visuals', {}) or {}
        cnt = 0
        for k, v in visuals.items():
            try:
                if isinstance(v, dict):
                    val = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                else:
                    val = int(v)
                if val == inst_id:
                    cnt += 1
            except (AttributeError, TypeError, ValueError):
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
            except (AttributeError, TypeError, ValueError):
                continue
        for _, val in visuals.items():
            try:
                if isinstance(val, dict):
                    vid = int(val.get('instance_id') or val.get('id') or val.get('building_instance_id'))
                else:
                    vid = int(val)
            except (AttributeError, TypeError, ValueError):
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
            except (AttributeError, TypeError, ValueError):
                continue
        if src is None:
            return None
        # Compute next id
        next_id = 1
        try:
            ids = [int(e.get('id')) for e in data if e.get('id') is not None]
            if ids:
                next_id = max(ids) + 1
        except (AttributeError, TypeError, ValueError):
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
        else:
            clone['overrides'] = {}
        # Tag as spawner visual to protect from global building saves
        try:
            if isinstance(self.model.selected_instance, dict):
                sid = str(self.model.selected_instance.get('id')) if self.model.selected_instance.get('id') is not None else None
            else:
                sid = None
            if isinstance(clone.get('overrides'), dict):
                clone['overrides']['_is_spawner_visual'] = True
                if sid:
                    clone['overrides']['spawner_instance_id'] = sid
            # Also persist root-level spawn identifiers so loader/saver can preserve IDs
            if sid:
                clone['spawn_id'] = str(sid)
                clone['spawner_instance_id'] = str(sid)
        except (AttributeError, TypeError, ValueError):
            pass
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
        vti = getattr(self.visuals.model, 'text_input', None)
        if vti is None or vti.active:
            return False
        new_txt = vti.text if vti else ''
        self.model.visuals_pending_templates[display_state] = new_txt
        ok, msg, new_tpl_id = self._validate_template_text(new_txt)
        if not ok and new_txt.strip() != '':
            try:
                if vti is not None:
                    vti.activate(new_txt, select_all=False)
            except (AttributeError, TypeError, ValueError):
                pass
            return True
        # If empty text -> clear mapping for this state
        if new_txt.strip() == '':
            try:
                key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                json_key = key_map.get(display_state, display_state)
                visuals = dict(getattr(self.model, 'visuals', {}) or {})
                if json_key in visuals:
                    visuals.pop(json_key, None)
                    self.model.visuals = visuals
                    try:
                        if self.model.selected_instance is not None:
                            self.model.selected_instance['visuals'] = visuals
                    except AttributeError:
                        pass
                    # Persist and refresh
                    self._persist_instance()
                    self._build_visuals_rows()
            except (AttributeError, TypeError, ValueError):
                pass
            # Exit edit mode and stop OS text input
            self.model.visuals_editing_state = None
            try:
                import pygame as _pg
                _pg.key.stop_text_input()
            except (ImportError, AttributeError, pygame.error):
                pass
            return True
        # Apply the new template id using the same helper as the picker flow
        try:
            if new_tpl_id is not None:
                # Debounce sanitization during create/reuse/update to avoid losing visuals before persist
                try:
                    import pygame as _pg
                    self._sanitize_block_until_ms = int((_pg.time.get_ticks() or 0) + 600)
                except (ImportError, AttributeError, TypeError, ValueError):
                    self._sanitize_block_until_ms = 0
                self.set_visual_template_via_picker(str(display_state), int(new_tpl_id))
        except (AttributeError, TypeError, ValueError):
            pass
        # Exit edit mode and stop OS text input
        self.model.visuals_editing_state = None
        try:
            import pygame as _pg
            _pg.key.stop_text_input()
        except (ImportError, AttributeError, pygame.error):
            pass
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
        except (AttributeError, TypeError, ValueError):
            pass
        # Current visuals row info
        rows = self.get_visuals_rows()
        cur_inst_int: Optional[int] = None
        for st, inst_str, _tpl in rows:
            if st == state_key:
                try:
                    cur_inst_int = int(float(str(inst_str))) if str(inst_str).strip() != '' and str(inst_str).upper() != 'N/A' else None
                except (ValueError, TypeError):
                    cur_inst_int = None
                break
        self._log.debug(f"[InstanceProps] set_visual_template_via_picker: state={state_key} tpl={new_tpl_id} cur_inst={cur_inst_int}")
        # Validación extra: si el instance_id actual no existe en el índice de buildings ni en el mundo,
        # trátalo como ausente para forzar la ruta de creación de una nueva instancia coherente.
        try:
            self._ensure_buildings_index()
            _exists_on_disk = bool((self._building_index or {}).get(int(cur_inst_int)) is not None) if cur_inst_int is not None else False
        except (AttributeError, TypeError, ValueError):
            _exists_on_disk = False
        try:
            _exists_in_world = self._find_building_entity_by_id(int(cur_inst_int)) is not None if cur_inst_int is not None else False
        except (AttributeError, TypeError, ValueError):
            _exists_in_world = False
        if cur_inst_int is not None and not (_exists_on_disk or _exists_in_world):
            try:
                self._log.warning(f"[InstanceProps] set_visual_template_via_picker: instance_id {cur_inst_int} no existe (disco/mundo). Se creará uno nuevo.")
            except (AttributeError, TypeError, ValueError):
                pass
            cur_inst_int = None
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
                    except (AttributeError, TypeError, ValueError):
                        continue
                if changed:
                    self._write_buildings_instances(data)
                    # refresh index/rows
                    self._building_index = None
                    self._ensure_buildings_index()
                    # Also ensure the updated entry carries root-level spawn identifiers
                    try:
                        sid = None
                        try:
                            inst_sel = self.model.selected_instance
                            if isinstance(inst_sel, dict) and inst_sel.get('id') is not None:
                                sid = str(inst_sel.get('id'))
                        except (AttributeError, TypeError, ValueError):
                            sid = None
                        if sid:
                            for e in data:
                                try:
                                    if int(e.get('id')) == cur_inst_int:
                                        e['spawn_id'] = sid
                                        e['spawner_instance_id'] = sid
                                        # mirror in overrides tag
                                        ov = e.get('overrides') or {}
                                        ov['_is_spawner_visual'] = True
                                        ov['spawner_instance_id'] = sid
                                        e['overrides'] = ov
                                        break
                                except (AttributeError, TypeError, ValueError):
                                    continue
                            self._write_buildings_instances(data)
                    except (AttributeError, TypeError, ValueError):
                        pass
                    # Ensure visuals mapping stores template as well
                    visuals = getattr(self.model, 'visuals', {}) or {}
                    key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                    json_key = key_map.get(state_key, state_key)
                    visuals[json_key] = {'instance_id': cur_inst_int, 'template_id': int(desired)}
                    self.model.visuals = visuals
                    # Ensure runtime will render spawner visuals
                    try:
                        inst = self.model.selected_instance
                        if isinstance(inst, dict):
                            ov = dict(inst.get('overrides') or {})
                            ov['visible_in_game'] = True
                            inst['overrides'] = ov
                    except (AttributeError, TypeError, ValueError):
                        pass
                    try:
                        if self.model.selected_instance is not None:
                            self.model.selected_instance['visuals'] = visuals
                    except AttributeError:
                        pass
                    self._build_visuals_rows()
                    # Persist spawner instance as well to keep ids/keys consistent
                    try:
                        self._persist_instance()
                    except (AttributeError, TypeError, ValueError):
                        pass
                    # Reveal the updated/assigned building in editor immediately
                    try:
                        self._tag_and_reveal_building(int(cur_inst_int), state_key)
                    except (AttributeError, TypeError, ValueError):
                        pass
                    self._log.info(f"[InstanceProps] Updated instance {cur_inst_int} -> template {desired}")
                    try:
                        # Log current row for state
                        for r in (self.model.visuals_rows or []):
                            if r[0] == state_key:
                                self._log.debug(f"[InstanceProps] Row after update: {r}")
                                break
                    except (AttributeError, TypeError, ValueError):
                        pass
            else:
                # Shared by multiple states: clone and rewire only this state
                new_id = self._clone_instance_with_new_template(cur_inst_int, int(desired))
                if new_id is not None:
                    visuals = getattr(self.model, 'visuals', {}) or {}
                    key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                    json_key = key_map.get(state_key, state_key)
                    visuals[json_key] = {'instance_id': new_id, 'template_id': int(desired)}
                    self.model.visuals = visuals
                    # Ensure runtime will render spawner visuals
                    try:
                        inst = self.model.selected_instance
                        if isinstance(inst, dict):
                            ov = dict(inst.get('overrides') or {})
                            ov['visible_in_game'] = True
                            inst['overrides'] = ov
                    except (AttributeError, TypeError, ValueError):
                        pass
                    try:
                        if self.model.selected_instance is not None:
                            self.model.selected_instance['visuals'] = visuals
                    except AttributeError:
                        pass
                    self._persist_instance()
                    # Rebuild views/indexes
                    self._ensure_buildings_index()
                    self._build_visuals_rows()
                    # Reveal newly cloned building in editor
                    try:
                        self._tag_and_reveal_building(int(new_id), state_key)
                    except (AttributeError, TypeError, ValueError):
                        pass
                    self._log.info(f"[InstanceProps] Cloned instance {cur_inst_int} -> new_id {new_id} tpl {desired} for state {state_key}")
                    try:
                        for r in (self.model.visuals_rows or []):
                            if r[0] == state_key:
                                self._log.debug(f"[InstanceProps] Row after clone: {r}")
                                break
                    except (AttributeError, TypeError, ValueError):
                        pass
            return
        # If there was no instance id and user provided a valid template id: ALWAYS create centered
        if cur_inst_int is None and new_tpl_id is not None:
            desired = int(new_tpl_id)
            # Prime pending input so add_building_instance_for_visual uses it
            try:
                self.model.visuals_pending_templates[state_key] = str(desired)
            except (AttributeError, TypeError, ValueError):
                pass
            # Create a new instance centered on the owning spawner
            new_id = self.add_building_instance_for_visual(state_key, reveal=False)
            self._log.info(f"[InstanceProps] Created new centered instance {new_id} for state {state_key} tpl {desired}")
            try:
                for r in (self.model.visuals_rows or []):
                    if r[0] == state_key:
                        self._log.debug(f"[InstanceProps] Row after create: {r}")
                        break
            except Exception:
                pass
            # Reload disk snapshot (add_building_instance_for_visual already attempts it)
            try:
                self._reload_selected_from_json()
            except (AttributeError, OSError, ValueError, TypeError):
                pass
        # also toast here as a safety (if picker flow ends here)
        try:
            self._show_toast(f"Template aplicado: {int(new_tpl_id)} → {state_key}")
        except (AttributeError, TypeError, ValueError):
            pass
        # Done
        return
    def add_building_instance_for_visual(self, state_key: str, reveal: bool = True) -> Optional[int]:
        # Need a template id: prefer current text input if editing this state
        txt = (self.model.visuals_pending_templates or {}).get(state_key, '')
        if getattr(self.model, 'visuals_editing_state', None) == state_key:
            vti = getattr(self.visuals.model, 'text_input', None)
            if vti is not None:
                try:
                    txt = vti.text
                except AttributeError:
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
            visuals[json_key] = {'instance_id': reuse_id, 'template_id': int(tpl_id)}
            self.model.visuals = visuals
            try:
                if self.model.selected_instance is not None:
                    self.model.selected_instance['visuals'] = visuals
            except AttributeError:
                pass
            self._persist_instance()
            # Refresh indexes/rows
            self._building_index = None
            self._ensure_buildings_index()
            self._build_visuals_rows()
            if reveal:
                try:
                    self._tag_and_reveal_building(int(reuse_id), state_key)
                except (AttributeError, TypeError, ValueError):
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
        # Determine zone and rel_x/rel_y from the selected spawner instance tile (zone-local)
        zone = None
        local_tile = (0, 0)
        try:
            zone = str((self.model.selected_instance or {}).get('zone'))
        except (AttributeError, TypeError, ValueError):
            zone = None
        try:
            t = (self.model.selected_instance or {}).get('tile', (0, 0))
            if isinstance(t, (list, tuple)) and len(t) >= 2:
                local_tile = (int(t[0]), int(t[1]))
        except (AttributeError, TypeError, ValueError):
            local_tile = (0, 0)
        if not zone:
            zone = 'lobby'
        # Convert the zone-local tile to pixels relative to the zone origin
        try:
            rel_x = int(local_tile[0] * TILE_SIZE)
            rel_y = int(local_tile[1] * TILE_SIZE)
        except (TypeError, ValueError):
            rel_x = 0
            rel_y = 0
        # Attempt to reuse an existing instance in the same spot and template to avoid duplicates
        try:
            zone_norm = zone
            desired_tid = int(tpl_id)
            best_id = None
            best_score = (-1, -1)
            for e in data:
                try:
                    if int(e.get('template_id')) != desired_tid:
                        continue
                    if str(e.get('zone') or 'lobby') != str(zone_norm):
                        continue
                    if int(e.get('rel_x') or 0) != int(local_tile[0] * TILE_SIZE):
                        continue
                    if int(e.get('rel_y') or 0) != int(local_tile[1] * TILE_SIZE):
                        continue
                    ov = e.get('overrides') if isinstance(e, dict) else {}
                    # Score: prefer tagged spawner visuals tied to this spawner, then any tagged, then lowest id
                    tied = 0
                    try:
                        sid = str((self.model.selected_instance or {}).get('id')) if (self.model.selected_instance or {}).get('id') is not None else None
                        if sid and (str(e.get('spawner_instance_id')) == sid or str((ov or {}).get('spawner_instance_id')) == sid):
                            tied = 1
                    except (AttributeError, TypeError, ValueError):
                        tied = 0
                    is_tag = 1 if (isinstance(ov, dict) and bool(ov.get('_is_spawner_visual'))) else 0
                    score = (tied, is_tag)
                    if score > best_score:
                        best_score = score
                        best_id = int(e.get('id'))
                except (AttributeError, TypeError, ValueError):
                    continue
            if best_id is not None:
                visuals = getattr(self.model, 'visuals', {}) or {}
                key_map = getattr(self.model, 'visuals_key_map', {}) or {}
                json_key = key_map.get(state_key, state_key)
                visuals[json_key] = {'instance_id': best_id, 'template_id': int(tpl_id)}
                self.model.visuals = visuals
                try:
                    if self.model.selected_instance is not None:
                        self.model.selected_instance['visuals'] = visuals
                except AttributeError:
                    pass
                self._persist_instance()
                # Refresh indexes/rows
                self._building_index = None
                self._ensure_buildings_index()
                self._build_visuals_rows()
                if reveal:
                    try:
                        self._tag_and_reveal_building(int(best_id), state_key)
                    except (AttributeError, TypeError, ValueError):
                        pass
                return best_id
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            self._log.debug(f"[InstanceProps] No reusable instance found -> creating new (zone={zone}, tile={local_tile}, tpl={tpl_id})")
        except (AttributeError, TypeError, ValueError):
            pass
        # Center the new building on the spawner center (tile center), using the alpha bounding box
        # of the scaled sprite (so transparent margins don't bias centering)
        try:
            spawn_cx = int(rel_x + (TILE_SIZE // 2))
            spawn_cy = int(rel_y + (TILE_SIZE // 2))
            # Determine desired visual size (width,height)
            w: int | None = None
            h: int | None = None
            brx = bry = 0
            brw = brh = None
            anchor_mode = 'content_center'
            # Prefer templates original_scale
            try:
                for tentry in svc_load_buildings_templates():
                    try:
                        if int(tentry.get('id')) == int(tpl_id):
                            oscale = tentry.get('original_scale')
                            try:
                                am = str(tentry.get('anchor_mode') or '')
                                if am:
                                    anchor_mode = am
                            except (AttributeError, TypeError, ValueError):
                                pass
                            if isinstance(oscale, (list, tuple)) and len(oscale) >= 2:
                                w = int(oscale[0]); h = int(oscale[1])
                                # Compute bounding box at that scale
                                try:
                                    img_path = svc_get_template_image_path(int(tpl_id))
                                    if img_path:
                                        import pygame as _pg
                                        raw = _pg.image.load(img_path)
                                        surf = _pg.transform.scale(raw, (int(w), int(h)))
                                        br = surf.get_bounding_rect(min_alpha=1)
                                        brx, bry, brw, brh = br.x, br.y, br.w, br.h
                                except (AttributeError, TypeError, ValueError, pygame.error):
                                    brw = brh = None
                            break
                    except (AttributeError, TypeError, ValueError):
                        continue
            except (AttributeError, TypeError, ValueError, OSError):
                pass
            # Fallback: probe image size and apply same auto-downscale rule (>512 -> quarter)
            if w is None or h is None:
                try:
                    img_path = svc_get_template_image_path(int(tpl_id))
                    if img_path:
                        import pygame as _pg
                        raw = _pg.image.load(img_path)
                        iw, ih = raw.get_size()
                        if iw > 512 or ih > 512:
                            iw //= 4; ih //= 4
                        w, h = int(iw), int(ih)
                        # Bounding rect from scaled surface
                        try:
                            surf = _pg.transform.scale(raw, (int(w), int(h)))
                            br = surf.get_bounding_rect(min_alpha=1)
                            brx, bry, brw, brh = br.x, br.y, br.w, br.h
                        except (AttributeError, TypeError, ValueError, pygame.error):
                            brw = brh = None
                except (AttributeError, TypeError, ValueError, OSError, pygame.error):
                    w = None; h = None
            if w is not None and h is not None and w > 0 and h > 0:
                if anchor_mode == 'base_center' and brw is not None and brh is not None and brw > 0 and brh > 0:
                    # Align bottom-center of visible content to spawn center
                    rel_x = int(spawn_cx - (brx + brw // 2))
                    rel_y = int(spawn_cy - (bry + brh))
                elif brw is not None and brh is not None and brw > 0 and brh > 0 and anchor_mode == 'content_center':
                    # Align bounding-rect center to spawn center
                    rel_x = int(spawn_cx - (brx + brw // 2))
                    rel_y = int(spawn_cy - (bry + brh // 2))
                else:
                    # Fallback: align image geometric center
                    rel_x = int(spawn_cx - (w // 2))
                    rel_y = int(spawn_cy - (h // 2))
        except (AttributeError, TypeError, ValueError):
            # If centering fails, keep top-left at tile origin as fallback
            pass
        entry = {
            'id': next_id,
            'template_id': tpl_id,
            'zone': zone,
            'rel_x': int(rel_x),
            'rel_y': int(rel_y),
        }
        # Tag as spawner visual to protect from global building saves
        try:
            if isinstance(self.model.selected_instance, dict):
                sid = str(self.model.selected_instance.get('id')) if self.model.selected_instance.get('id') is not None else None
            else:
                sid = None
            entry['overrides'] = entry.get('overrides') or {}
            entry['overrides']['_is_spawner_visual'] = True
            if sid:
                entry['overrides']['spawner_instance_id'] = sid
            # Persist computed scale if available so runtime size matches centering
            try:
                if 'overrides' in entry:
                    # Persist the scale we used for centering so runtime matches
                    if locals().get('w') is not None and locals().get('h') is not None and int(locals()['w']) > 0 and int(locals()['h']) > 0:
                        entry['overrides']['scale'] = [int(locals()['w']), int(locals()['h'])]
            except (AttributeError, TypeError, ValueError):
                pass
            # Also include root-level identifiers so save_buildings_split preserves this instance id
            try:
                if sid:
                    entry['spawn_id'] = str(sid)
                    entry['spawner_instance_id'] = str(sid)
            except (AttributeError, TypeError, ValueError):
                pass
        except (AttributeError, TypeError, ValueError):
            pass
        data.append(entry)
        self._write_buildings_instances(data)
        # Post-create correction: load the building and recenter using its actual scaled image's
        # alpha bounding box (avoid transparent margins)
        try:
            # Ensure building is present in world
            try:
                self.visuals._ensure_building_loaded(int(next_id))
            except (AttributeError, TypeError, ValueError):
                pass
            ob = None
            try:
                ob = self.visuals._find_building_entity_by_id(int(next_id))
            except (AttributeError, TypeError, ValueError):
                ob = None
            if ob is not None:
                # Get the actual scaled surface and its alpha bounding rect
                surf = getattr(getattr(ob, 'model', None), 'image', None)
                br = None
                try:
                    if surf is not None:
                        br = surf.get_bounding_rect(min_alpha=1)
                except (AttributeError, TypeError, ValueError):
                    br = None
                if br is not None and br.w > 0 and br.h > 0:
                    # Recompute spawn center (tile center) and correct rels to align bounding center
                    spawn_cx = int((local_tile[0] * TILE_SIZE) + (TILE_SIZE // 2))
                    spawn_cy = int((local_tile[1] * TILE_SIZE) + (TILE_SIZE // 2))
                    corr_rx = int(spawn_cx - (br.x + br.w // 2))
                    corr_ry = int(spawn_cy - (br.y + br.h // 2))
                    # Patch JSON entry
                    for e in data:
                        try:
                            if int(e.get('id')) == int(next_id):
                                e['rel_x'] = corr_rx
                                e['rel_y'] = corr_ry
                                break
                        except (AttributeError, TypeError, ValueError):
                            continue
                    self._write_buildings_instances(data)
                    # Update in-world object
                    try:
                        setattr(getattr(ob, 'model', ob), 'rel_x', corr_rx)
                        setattr(getattr(ob, 'model', ob), 'rel_y', corr_ry)
                    except (AttributeError, TypeError, ValueError):
                        pass
        except (AttributeError, TypeError, ValueError):
            pass
        # Update visuals mapping and persist spawner instance
        visuals = getattr(self.model, 'visuals', {}) or {}
        # Map displayed canonical to actual JSON key if present; otherwise use the displayed key
        key_map = getattr(self.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        visuals[json_key] = {'instance_id': next_id, 'template_id': int(tpl_id)}
        try:
            self._log.debug(f"[InstanceProps] add_building_instance_for_visual: set visuals[{json_key}]={next_id}")
        except (AttributeError, TypeError, ValueError):
            pass
        self.model.visuals = visuals
        # Ensure runtime will render spawner visuals
        try:
            inst = self.model.selected_instance
            if isinstance(inst, dict):
                ov = dict(inst.get('overrides') or {})
                ov['visible_in_game'] = True
                inst['overrides'] = ov
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            if self.model.selected_instance is not None:
                self.model.selected_instance['visuals'] = visuals
        except AttributeError:
            pass
        try:
            self._log.debug(f"[InstanceProps] add_building_instance_for_visual: model.visuals now={self.model.visuals}")
        except (AttributeError, TypeError, ValueError):
            pass
        self._persist_instance()
        # Reload from disk to be 100% sure it persisted and update UI model
        try:
            self._reload_selected_from_json()
        except (AttributeError, OSError, ValueError, TypeError):
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
            except (AttributeError, TypeError, ValueError):
                pass
        return next_id
    def clear_visual_for_state(self, state_key: str) -> None:
        """Remove the visual mapping for a given state and clean JSON files.
        - Removes visuals[state_key] from the selected spawner instance and persists
          to data/spawners/spawners_instances.json
        - If that mapping referenced a building instance id, and the corresponding
          buildings_instances.json entry is tagged as a spawner visual for this
          spawner instance, remove it from data/buildings/buildings_instances.json
        - Hides the building in the world (editor visibility) if present
        """
        visuals = dict(getattr(self.model, 'visuals', {}) or {})
        if not visuals:
            return
        # Map display key -> JSON key
        key_map = getattr(self.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        v = visuals.get(json_key)
        if v is None:
            return
        # Parse building instance id if present
        bid = None
        try:
            if isinstance(v, dict):
                bid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
            else:
                bid = int(v)
        except (AttributeError, TypeError, ValueError):
            bid = None
        # Remove mapping and persist spawner instance
        visuals.pop(json_key, None)
        self.model.visuals = visuals
        try:
            if self.model.selected_instance is not None:
                self.model.selected_instance['visuals'] = visuals
        except AttributeError:
            pass
        # Persist to spawners_instances.json
        self._persist_instance()
        # Reload from disk to ensure UI reflects persisted state
        try:
            self._reload_selected_from_json()
        except (AttributeError, OSError, ValueError, TypeError):
            pass
        # Attempt to remove the building instance from buildings_instances.json if it is ours
        if bid is not None:
            # Strict mode safety: if no other spawner instance references this building id,
            # we can delete it even if it lacks tagging/root linkage.
            referenced_elsewhere = False
            try:
                all_inst = load_instances_json()
                for _inst in all_inst or []:
                    try:
                        vis = _inst.get('visuals')
                        if not isinstance(vis, dict) or not vis:
                            continue
                        for _k, _v in list(vis.items()):
                            try:
                                if isinstance(_v, dict):
                                    _vid = _v.get('instance_id') or _v.get('id') or _v.get('building_instance_id')
                                    _vid = int(_vid) if _vid is not None else None
                                else:
                                    _vid = int(_v)
                            except Exception:
                                _vid = None
                            if _vid is not None and int(_vid) == int(bid):
                                referenced_elsewhere = True
                                break
                        if referenced_elsewhere:
                            break
                    except Exception:
                        continue
            except Exception:
                referenced_elsewhere = False
            data = self._load_buildings_instances()
            sid = None
            try:
                inst = self.model.selected_instance or {}
                sid = str(inst.get('id')) if inst.get('id') is not None else None
            except Exception:
                sid = None
            kept = []
            removed = False
            for e in data:
                try:
                    eid = int(e.get('id'))
                except Exception:
                    kept.append(e)
                    continue
                if eid != int(bid):
                    kept.append(e)
                    continue
                # Match only tagged spawner visuals for this spawner instance
                ov = e.get('overrides') if isinstance(e, dict) else None
                is_tagged = False
                try:
                    if isinstance(ov, dict) and ov.get('_is_spawner_visual') and (sid is None or str(ov.get('spawner_instance_id')) == str(sid)):
                        is_tagged = True
                except Exception:
                    is_tagged = False
                # Strict mode: also consider root-level identifiers as linkage to this spawner
                is_root_linked = False
                try:
                    if sid is not None and (str(e.get('spawner_instance_id')) == str(sid) or str(e.get('spawn_id')) == str(sid)):
                        is_root_linked = True
                except Exception:
                    is_root_linked = False
                # Remove if explicitly tagged, or in strict mode if root linkage matches,
                # or if in strict mode and no other spawner references this building id anymore
                if is_tagged or (bool(getattr(self, 'strict_visuals_cleanup', False)) and (is_root_linked or not referenced_elsewhere)):
                    removed = True
                    continue  # drop this entry
                # If not tagged, keep it to avoid deleting user buildings
                kept.append(e)
            if removed:
                self._write_buildings_instances(kept)
                # Refresh index for subsequent checks
                self._building_index = None
                self._ensure_buildings_index()
                # Remove building from world/editor entirely
                try:
                    self._remove_building_entity_by_id(int(bid))
                except Exception:
                    pass
            # Rebuild rows/UI (already reloaded from disk above)
            self._build_visuals_rows()
            try:
                self._log.info(f"[InstanceProps] Cleared visual for state={state_key}; removed_building_id={bid}")
            except Exception:
                pass

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
        except (ValueError, TypeError):
            pass
        # JSON/list/dict
        if (t.startswith('[') and t.endswith(']')) or (t.startswith('{') and t.endswith('}')):
            try:
                import json
                return json.loads(t)
            except (ValueError, TypeError):
                try:
                    return ast.literal_eval(t)
                except (ValueError, SyntaxError):
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
            except (ValueError, TypeError):
                idx = None
            if idx is not None:
                tile = inst.get('tile')
                if not isinstance(tile, list):
                    tile = [0, 0]
                while len(tile) <= idx:
                    tile.append(0)
                try:
                    tile[idx] = int(value)
                except (ValueError, TypeError):
                    try:
                        tile[idx] = int(float(value))
                    except (ValueError, TypeError):
                        pass

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
            except (TypeError, ValueError):
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
        except (AttributeError, TypeError, ValueError):
            pass
        # Ensure the in-memory visuals map from the model is applied before persisting
        try:
            visuals_model = getattr(self.model, 'visuals', None)
            if isinstance(visuals_model, dict):
                # Normalize visuals to new format {instance_id, template_id}
                self._ensure_buildings_index()
                idx = self._building_index or {}
                norm: dict[str, dict] = {}
                for k, v in (visuals_model or {}).items():
                    try:
                        if isinstance(v, dict):
                            # Ensure keys and fill missing template_id
                            vid = None
                            try:
                                vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                            except (TypeError, ValueError, AttributeError):
                                vid = None
                            tpl = v.get('template_id')
                            if tpl is None and vid is not None and vid in idx:
                                tpl = idx.get(vid)
                            norm[str(k)] = {'instance_id': vid if vid is not None else v, 'template_id': tpl}
                        else:
                            vid = int(v)
                            tpl = idx.get(vid)
                            norm[str(k)] = {'instance_id': vid, 'template_id': tpl}
                    except (AttributeError, TypeError, ValueError):
                        # Keep as-is if cannot normalize
                        norm[str(k)] = {'instance_id': v, 'template_id': None}
                try:
                    self._log.debug(f"[InstanceProps] _persist_instance: computed norm_visuals={norm}")
                except (AttributeError, TypeError, ValueError):
                    pass
                # Guard: avoid wiping visuals unintentionally when model.visuals is empty transiently
                if norm:
                    if inst.get('visuals') != norm:
                        inst['visuals'] = norm
                else:
                    # If norm is empty but instance already has visuals with entries, keep them
                    try:
                        cur_vis = inst.get('visuals')
                        if isinstance(cur_vis, dict) and len(cur_vis) > 0:
                            try:
                                self._log.debug("[InstanceProps] _persist_instance: norm empty, KEEP existing visuals (non-empty)")
                            except (AttributeError, TypeError, ValueError):
                                pass
                        else:
                            inst['visuals'] = {}
                    except (AttributeError, TypeError, ValueError):
                        inst['visuals'] = {}
        except (AttributeError, TypeError, ValueError):
            try:
                visuals_model = getattr(self.model, 'visuals', None)
                if isinstance(visuals_model, dict):
                    inst['visuals'] = visuals_model
            except (AttributeError, TypeError, ValueError):
                pass
        # Reload data fresh
        data = load_instances_json()

        # Compute identities
        cur_id = None
        try:
            cur_id = str(inst.get('id')) if inst.get('id') is not None else None
        except (AttributeError, TypeError, ValueError):
            cur_id = None
        cur_key = None
        try:
            tpl = str(inst.get('template_id'))
            zone = str(inst.get('zone'))
            tile = tuple(inst.get('tile', [0, 0]))
            cur_key = (tpl, zone, (int(tile[0]), int(tile[1])))
        except (AttributeError, TypeError, ValueError):
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
                except (AttributeError, TypeError, ValueError):
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
                except (AttributeError, TypeError, ValueError):
                    continue
        try:
            self._log.debug(f"[InstanceProps] _persist_instance: resolve target_idx={target_idx} original_id={self.model.original_id} selected_index={self.model.selected_index} original_key={self.model.original_key} cur_key={cur_key}")
        except (AttributeError, TypeError, ValueError):
            pass

        # Ensure a unique 'id' for the instance (handle rename conflicts)
        existing_ids = {str(e.get('id')) for e in data if e.get('id')}
        if target_idx is not None:
            # Exclude current target from conflict set
            try:
                existing_ids.discard(str(data[target_idx].get('id')))
            except (AttributeError, TypeError, ValueError):
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
        # Verify round-trip persisted visuals; if lost accidentally, rewrite once with model snapshot
        try:
            check, idx_check, _ = find_instance_by_id(str(inst.get('id')))
            if idx_check is not None:
                on_disk = check[idx_check].get('visuals')
                desired = inst.get('visuals')
                if isinstance(desired, dict) and desired and (not isinstance(on_disk, dict) or len(on_disk or {}) < len(desired)):
                    check[idx_check]['visuals'] = desired
                    write_instances_json(check)
                    try:
                        self._log.warning("[InstanceProps] _persist_instance: on-disk visuals were smaller/empty; rewrote with in-memory snapshot")
                    except (AttributeError, TypeError, ValueError):
                        pass
        except (AttributeError, TypeError, ValueError, OSError):
            pass
        try:
            self._log.debug(f"[InstanceProps] _persist_instance: wrote instance id={inst.get('id')} with visuals keys={list((inst.get('visuals') or {}).keys()) if isinstance(inst.get('visuals'), dict) else inst.get('visuals')}")
        except (AttributeError, TypeError, ValueError):
            pass
        # Update original ids/keys for subsequent edits
        self.model.original_id = str(inst.get('id')) if inst and inst.get('id') is not None else None
        self.model.original_key = cur_key
        # Notify UI to refresh instances list if requested
        try:
            if self.on_persist is not None:
                self.on_persist()
        except AttributeError:
            pass
        # Notify editor about saved instance with context (changed key)
        try:
            if self.on_instance_saved is not None:
                self.on_instance_saved(inst, getattr(self, '_last_edit_key', None))
        except AttributeError:
            pass

        # Clear last edit key after notifying
        self._last_edit_key = None

__all__ = ["InstancePropertiesController"]
