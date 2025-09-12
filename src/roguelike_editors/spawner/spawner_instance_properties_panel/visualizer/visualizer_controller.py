from __future__ import annotations

from typing import Optional, Any, List, Dict
from roguelike_engine.buildings.factory import build_from_config
from ..services.buildings_service import (
    load_buildings_instances,
    get_template_image_path,
)

from .visualizer_model import VisualizerModel
from .visualizer_view import VisualizerView
from .visualizer_events import VisualizerEvents


class VisualizerController:
    """Feature controller for the Visuals table inside Instance Properties.

    It owns its own MVC (model/view/events) but delegates data mutations and
    persistence to the parent InstancePropertiesController.
    """

    def __init__(self, parent_controller) -> None:
        # Keep a dynamic reference to the parent controller without importing it here
        self.parent = parent_controller
        self.model = VisualizerModel()
        self.view = VisualizerView(self)
        self.events = VisualizerEvents()

    # --- Convenience accessors to parent data --------------------------------
    def get_visuals_rows(self):
        return self.parent.get_visuals_rows()

    def get_visuals(self) -> dict:
        return getattr(self.parent.model, 'visuals', {}) or {}

    def get_visuals_key_map(self) -> dict:
        return getattr(self.parent.model, 'visuals_key_map', {}) or {}

    def get_text_input(self):
        return self.parent.get_text_input()

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
        self.model.editor_visibility[int(bid)] = bool(visible)
        ob = self._find_building_entity_by_id(int(bid))
        if ob is not None:
            try:
                setattr(ob, 'visible', bool(visible))
            except Exception:
                pass
            try:
                setattr(ob, 'editor_hidden', not bool(visible))
            except Exception:
                pass

    def tag_and_reveal_building(self, bid: int, state_key: str) -> None:
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
                        if getattr(ob, 'spawn_id', None) == str(getattr(cfg, 'template_id', '')):
                            setattr(ob, '_spawner_eid', eid)
                            setattr(ob, '_world_ref', world)
                            break
                    except Exception:
                        continue
        except Exception:
            pass
        self._set_building_visible(int(bid), True)

    def is_building_visible_for_state(self, state_key: str) -> bool:
        visuals = getattr(self.parent.model, 'visuals', {}) or {}
        key_map = getattr(self.parent.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        bid = visuals.get(json_key)
        if bid is None:
            return True
        try:
            return bool(self.model.editor_visibility.get(int(bid), True))
        except Exception:
            return True

    def toggle_building_visibility_for_state(self, state_key: str) -> None:
        visuals = getattr(self.parent.model, 'visuals', {}) or {}
        key_map = getattr(self.parent.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        bid = visuals.get(json_key)
        if bid is None:
            return
        try:
            bid_int = int(bid)
        except Exception:
            return
        cur = bool(self.model.editor_visibility.get(bid_int, True))
        self._set_building_visible(bid_int, not cur)

    # --- Actions that change data are delegated back to parent ----------------
    def open_picker(self, state_key: str) -> None:
        self.parent.open_visuals_picker_for_state(state_key)

    def add_instance_for_state(self, state_key: str, *, reveal: bool = True) -> None:
        self.parent.add_building_instance_for_visual(state_key, reveal=reveal)

    def begin_edit_visual(self, state_key: str) -> None:
        self.parent.begin_edit_visual(state_key)

    def cancel_edit_visual(self) -> None:
        self.parent.cancel_edit_visual()

    def commit_visual_edit_if_finished(self) -> bool:
        return self.parent.commit_visual_edit_if_finished()

    def validate_template_text(self, text: str):
        return self.parent.get_visual_input_validation(str(text))


__all__ = ["VisualizerController"]
