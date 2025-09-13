from __future__ import annotations

from typing import Optional, Any, List, Dict
from roguelike_engine.buildings.factory import build_from_config
from ..services.buildings_service import (
    load_buildings_instances,
    get_template_image_path,
)
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

from .visuals_model import VisualsModel
from .visuals_view import VisualsView
from .visuals_events import VisualsEvents


class VisualsController:
    """Feature controller for the Visuals table inside Instance Properties.

    It owns its own MVC (model/view/events) but delegates data mutations and
    persistence to the parent InstancePropertiesController.
    """

    def __init__(self, parent_controller) -> None:
        # Keep a dynamic reference to the parent controller without importing it here
        self.parent = parent_controller
        self.model = VisualsModel()
        self.view = VisualsView(self)
        self.events = VisualsEvents()

    # --- Convenience accessors to parent data --------------------------------
    def get_visuals_rows(self):
        return self.parent.get_visuals_rows()

    def get_visuals(self) -> dict:
        return getattr(self.parent.model, 'visuals', {}) or {}

    def get_visuals_key_map(self) -> dict:
        return getattr(self.parent.model, 'visuals_key_map', {}) or {}

    def get_text_input(self):
        return self.parent.get_text_input()

    # --- Visuals mapping helpers --------------------------------------------
    def _resolve_json_key_for_state(self, state_key: str) -> str:
        """Given a display state name (TitleCase), resolve the actual JSON key used."""
        try:
            key_map = getattr(self.parent.model, 'visuals_key_map', {}) or {}
            return str(key_map.get(state_key, state_key))
        except Exception:
            return str(state_key)

    def _get_mapping_entry_for_state(self, state_key: str):
        """Return the raw mapping value from model.visuals for the given state (dict | int | None)."""
        try:
            visuals = getattr(self.parent.model, 'visuals', {}) or {}
        except Exception:
            visuals = {}
        try:
            return visuals.get(self._resolve_json_key_for_state(state_key))
        except Exception:
            return None

    def _get_instance_id_for_state(self, state_key: str) -> int | None:
        """Extract instance_id as int from the mapping of a state, if present and valid."""
        raw = self._get_mapping_entry_for_state(state_key)
        try:
            if raw is None:
                return None
            if isinstance(raw, dict):
                return int(raw.get('instance_id') or raw.get('id') or raw.get('building_instance_id'))
            return int(raw)
        except Exception:
            return None

    def _get_template_id_for_state(self, state_key: str) -> int | None:
        """Best-effort template_id resolution for a state: prefer explicit mapping, fallback to building index."""
        raw = self._get_mapping_entry_for_state(state_key)
        try:
            if isinstance(raw, dict) and raw.get('template_id') is not None:
                return int(raw.get('template_id'))
        except Exception:
            pass
        # Fallback via buildings index if instance_id present
        try:
            bid = self._get_instance_id_for_state(state_key)
            idx = getattr(self.parent, '_building_index', {}) or {}
            if bid is not None and int(bid) in idx:
                tid_str = idx.get(int(bid))
                return int(tid_str) if tid_str is not None else None
        except Exception:
            pass
        return None

    # --- World/buildings helpers ---------------------------------------------
    def _get_world(self):
        try:
            return getattr(getattr(self.parent.game, 'ecs', None), 'ecs_world', None)
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

    def _get_selected_spawner_id(self) -> str | None:
        try:
            inst = getattr(self.parent.model, 'selected_instance', None)
            if isinstance(inst, dict) and inst.get('id') is not None:
                return str(inst.get('id'))
        except Exception:
            return None
        return None

    def _find_visual_entity_for_state(self, state_key: str):
        """Best-effort resolver for the visual entity of a given state.
        Priority:
        1) World object tagged as spawner visual for this spawner and state
        2) Fallback to instance_id mapping if present
        """
        sid = self._get_selected_spawner_id()
        # 1) Try tags (_is_spawner_visual + spawner_instance_id + state_key)
        if sid is not None:
            for ob in self._iter_building_entities():
                try:
                    if not getattr(ob, '_is_spawner_visual', False):
                        continue
                    if str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', ''))) != str(sid):
                        continue
                    if str(getattr(ob, 'spawner_state_key', '')) == str(state_key):
                        return ob
                except Exception:
                    continue
        # 2) Fallback by instance_id from visuals mapping
        try:
            visuals = getattr(self.parent.model, 'visuals', {}) or {}
            key_map = getattr(self.parent.model, 'visuals_key_map', {}) or {}
            json_key = key_map.get(state_key, state_key)
            raw = visuals.get(json_key)
            if raw is not None:
                if isinstance(raw, dict):
                    bid = int(raw.get('instance_id') or raw.get('id') or raw.get('building_instance_id'))
                else:
                    bid = int(raw)
                return self._find_building_entity_by_id(int(bid))
        except Exception:
            pass
        return None

    def center_camera_on_state(self, state_key: str) -> None:
        """Center camera on the building instance mapped to the given visuals state key.
        Best-effort: ensures the building is loaded, then computes world pixel position
        from buildings_instances.json using zone offsets and rel_x/rel_y.
        """
        # Prefer the live world entity if present (robust across restarts)
        try:
            cam = getattr(self.parent.game, 'camera', None)
            if cam is None:
                return None
            ob = self._find_visual_entity_for_state(state_key)
            if ob is not None:
                try:
                    zone = getattr(ob, 'zone', None)
                    if zone is None:
                        zone = getattr(getattr(ob, 'model', ob), 'zone', None)
                    if not zone:
                        zone = 'lobby'
                    rx = getattr(getattr(ob, 'model', ob), 'rel_x', None)
                    ry = getattr(getattr(ob, 'model', ob), 'rel_y', None)
                    if rx is None or ry is None:
                        # Fallback to JSON if coords are missing
                        raise RuntimeError('missing rel coords on entity')
                    off = global_map_settings.zone_offsets.get(str(zone), (0, 0))
                    bx = int(off[0] * TILE_SIZE) + int(rx)
                    by = int(off[1] * TILE_SIZE) + int(ry)
                    zoom = getattr(cam, 'zoom', 1.0) or 1.0
                    cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                    cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
                    return None
                except Exception:
                    pass
        except Exception:
            pass
        # Fallback: try to center using the instance id mapping and JSON
        bid = self._get_instance_id_for_state(state_key)
        if bid is None:
            return None
        try:
            self._ensure_building_loaded(int(bid))
        except Exception:
            pass
        try:
            cam = getattr(self.parent.game, 'camera', None)
            if cam is None:
                return None
            bx = by = None
            bzone = 'lobby'
            for e in load_buildings_instances():
                try:
                    if int(e.get('id')) == int(bid):
                        bzone = str(e.get('zone') or 'lobby')
                        rx = int(e.get('rel_x') or 0)
                        ry = int(e.get('rel_y') or 0)
                        off = global_map_settings.zone_offsets.get(bzone, (0, 0))
                        bx = int(off[0] * TILE_SIZE) + int(rx)
                        by = int(off[1] * TILE_SIZE) + int(ry)
                        break
                except Exception:
                    continue
            if bx is not None and by is not None:
                zoom = getattr(cam, 'zoom', 1.0) or 1.0
                cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
        except Exception:
            pass
        return None

    # JSON helpers now live in services

    def _ensure_building_loaded(self, bid: int) -> None:
        """If building with id 'bid' is not present in world.buildings, load it
        from instances/templates and append it. Editor-only best-effort."""
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
        for e in load_buildings_instances():
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
            cfg['image_path'] = get_template_image_path(int(inst_entry.get('template_id')))
            cfg['rel_x'] = int(inst_entry.get('rel_x', 0) or 0)
            cfg['rel_y'] = int(inst_entry.get('rel_y', 0) or 0)
            if inst_entry.get('zone') is not None:
                cfg['zone'] = str(inst_entry.get('zone'))
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
            cam = getattr(self.parent.game, 'camera', None)
            b = build_from_config(cfg, camera=cam)
            try:
                setattr(b, 'id', int(bid))
            except Exception:
                pass
            try:
                setattr(b, 'visible', True)
                setattr(b, 'editor_hidden', False)
                setattr(b, 'runtime_hidden', False)
            except Exception:
                pass
            try:
                if not hasattr(world, 'buildings') or world.buildings is None:
                    setattr(world, 'buildings', [])
                world.buildings.append(b)
            except Exception:
                pass
            try:
                ents = getattr(self.parent.game, 'entities', None)
                if ents is not None and hasattr(ents, 'buildings') and ents.buildings is not None:
                    ents.buildings.append(b)
            except Exception:
                pass
        except Exception:
            pass

    def _set_building_visible(self, bid: int, visible: bool) -> None:
        # Cache intended editor visibility
        self.model.editor_visibility[int(bid)] = bool(visible)
        ob = self._find_building_entity_by_id(int(bid))
        if ob is not None:
            # Do NOT touch runtime 'visible' flag; restrict to editor-only flag so gameplay is unaffected
            try:
                setattr(ob, 'editor_hidden', not bool(visible))
            except Exception:
                pass

    def tag_and_reveal_building(self, bid: int, state_key: str) -> None:
        ob = self._find_building_entity_by_id(int(bid))
        if ob is None:
            # Attempt to load the building instance into the world and retry
            try:
                self._ensure_building_loaded(int(bid))
            except Exception:
                pass
            ob = self._find_building_entity_by_id(int(bid))
            if ob is None:
                return
        try:
            setattr(ob, '_is_spawner_visual', True)
        except Exception:
            pass
        try:
            inst = self.parent.model.selected_instance or {}
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
        # Link back to ECS entity if present (best-effort)
        try:
            world = self._get_world()
            comps = getattr(world, 'components', {}) if world else {}
            if world and 'SpawnerConfig' in comps:
                for eid in world.get_entities_with('SpawnerConfig'):
                    try:
                        cfg = comps['SpawnerConfig'][eid]
                        # Match by spawner instance id if available
                        if getattr(ob, 'spawn_id', None) == str(getattr(cfg, 'instance_id', getattr(cfg, 'template_id', ''))):
                            setattr(ob, '_spawner_eid', eid)
                            setattr(ob, '_world_ref', world)
                            break
                    except Exception:
                        continue
        except Exception:
            pass
        self._set_building_visible(int(bid), True)
        # Center camera on the revealed building for user feedback
        try:
            cam = getattr(self.parent.game, 'camera', None)
            if cam is not None:
                # Find the building instance entry to compute world pixel position
                bx = by = None
                bzone = 'lobby'
                for e in load_buildings_instances():
                    try:
                        if int(e.get('id')) == int(bid):
                            bzone = str(e.get('zone') or 'lobby')
                            rx = int(e.get('rel_x') or 0)
                            ry = int(e.get('rel_y') or 0)
                            off = global_map_settings.zone_offsets.get(bzone, (0, 0))
                            bx = int(off[0] * TILE_SIZE) + int(rx)
                            by = int(off[1] * TILE_SIZE) + int(ry)
                            break
                    except Exception:
                        continue
                if bx is not None and by is not None:
                    zoom = getattr(cam, 'zoom', 1.0) or 1.0
                    cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                    cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
        except Exception:
            pass

    def is_building_visible_for_state(self, state_key: str) -> bool:
        # Prefer reading from the live entity (robust across restarts)
        ob = self._find_visual_entity_for_state(state_key)
        if ob is not None:
            try:
                hidden = bool(getattr(ob, 'editor_hidden', False))
                vis = bool(getattr(ob, 'visible', True)) and not hidden
                # Keep cache in model if id is available
                try:
                    bid = getattr(ob, 'id', None)
                    if bid is not None:
                        self.model.editor_visibility[int(bid)] = vis
                except Exception:
                    pass
                return vis
            except Exception:
                return True
        # Fallback to cached visibility by instance id
        bid_int = self._get_instance_id_for_state(state_key)
        if bid_int is None:
            return True
        return bool(self.model.editor_visibility.get(int(bid_int), True))

    def toggle_building_visibility_for_state(self, state_key: str) -> None:
        """Toggle only the editor rendering visibility for the building mapped to state_key.
        Robust even if the instance id is stale because global saver skipped it.
        """
        ob = self._find_visual_entity_for_state(state_key)
        if ob is not None:
            try:
                # Decide current effective visibility
                cur = (not bool(getattr(ob, 'editor_hidden', False)))
                new_vis = not cur
                try:
                    setattr(ob, 'editor_hidden', not bool(new_vis))
                except Exception:
                    pass
                try:
                    bid = getattr(ob, 'id', None)
                    if bid is not None:
                        self.model.editor_visibility[int(bid)] = bool(new_vis)
                except Exception:
                    pass
                return
            except Exception:
                pass
        # Fallback by instance id
        bid_int = self._get_instance_id_for_state(state_key)
        if bid_int is None:
            return
        cur = bool(self.model.editor_visibility.get(int(bid_int), True))
        self._set_building_visible(int(bid_int), not cur)

    def open_picker(self, state_key: str) -> None:
        """Delegates to parent to open the Visuals Picker for the given state."""
        self.parent.open_visuals_picker_for_state(state_key)

    def begin_edit_visual(self, state_key: str) -> None:
        self.parent.begin_edit_visual(state_key)

    def cancel_edit_visual(self) -> None:
        self.parent.cancel_edit_visual()

    def commit_visual_edit_if_finished(self) -> bool:
        return self.parent.commit_visual_edit_if_finished()

    def validate_template_text(self, text: str):
        return self.parent.get_visual_input_validation(str(text))

    def clear_visual_for_state(self, state_key: str) -> None:
        """Delegate clearing a visual mapping back to the parent controller."""
        self.parent.clear_visual_for_state(state_key)

    # --- Hard removal helper --------------------------------------------------
    def _remove_building_entity_by_id(self, bid: int) -> bool:
        """Remove any Building object with id 'bid' from the live world and editor lists.
        Returns True if any object was removed.
        """
        removed_any = False
        # ECS world list
        try:
            world = self._get_world()
            if world is not None and hasattr(world, 'buildings') and isinstance(world.buildings, list):
                arr = world.buildings
                for i in range(len(arr) - 1, -1, -1):
                    try:
                        if getattr(arr[i], 'id', None) == int(bid):
                            arr.pop(i)
                            removed_any = True
                    except Exception:
                        continue
        except Exception:
            pass
        # Editor/game registry if present
        try:
            ents = getattr(self.parent.game, 'entities', None)
            if ents is not None and hasattr(ents, 'buildings') and isinstance(ents.buildings, list):
                arr2 = ents.buildings
                for i in range(len(arr2) - 1, -1, -1):
                    try:
                        if getattr(arr2[i], 'id', None) == int(bid):
                            arr2.pop(i)
                            removed_any = True
                    except Exception:
                        continue
        except Exception:
            pass
        return removed_any


__all__ = ["VisualsController"]
