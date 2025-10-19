from __future__ import annotations

import logging
from typing import Any

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances,
    write_buildings_instances,
)

from . import world as world_svc
from . import mapping as mapping_svc
from . import building_loader as loader_svc
from . import visibility as vis_svc

logger = logging.getLogger(__name__)


def _apply_scale_from_mapping(controller, ob, state_key: str) -> None:
    try:
        raw = mapping_svc.get_mapping_entry_for_state(controller, state_key)
        if isinstance(raw, dict):
            sc = raw.get('scale')
            if isinstance(sc, (list, tuple)) and len(sc) == 2:
                w = int(sc[0])
                h = int(sc[1])
                if w > 0 and h > 0:
                    ob.resize(int(w), int(h))
    except Exception:
        pass


def _apply_split_ratio_from_mapping(controller, ob, state_key: str) -> None:
    try:
        raw = mapping_svc.get_mapping_entry_for_state(controller, state_key)
        if isinstance(raw, dict) and raw.get('split_ratio') is not None:
            try:
                sr = float(raw.get('split_ratio'))
            except (TypeError, ValueError):
                sr = None
            if sr is not None:
                ob.split_ratio = float(sr)
    except Exception:
        pass


def _compute_anchor_and_offset(controller, state_key: str) -> tuple[int, int, str]:
    inst = getattr(controller.parent.model, 'selected_instance', None)
    zone = 'lobby'
    if isinstance(inst, dict):
        zone = str(inst.get('zone') or 'lobby')
        tile = inst.get('tile') or (0, 0)
        try:
            tx, ty = int(tile[0]), int(tile[1])
        except Exception:
            tx, ty = 0, 0
        anchor_cx = int(tx * TILE_SIZE + TILE_SIZE // 2)
        anchor_cy = int(ty * TILE_SIZE + TILE_SIZE // 2)
    else:
        anchor_cx, anchor_cy = 0, 0
    off_dx, off_dy = 0, 0
    try:
        raw = mapping_svc.get_mapping_entry_for_state(controller, state_key)
        if isinstance(raw, dict):
            off = raw.get('offset')
            if isinstance(off, (list, tuple)) and len(off) == 2:
                off_dx = int(off[0])
                off_dy = int(off[1])
    except Exception:
        off_dx = off_dy = 0
    return int(anchor_cx + off_dx), int(anchor_cy + off_dy), zone


def tag_and_reveal_building(controller, bid: int, state_key: str) -> None:
    ob = world_svc.find_building_entity_by_id(controller, int(bid))
    if ob is None:
        try:
            loader_svc.ensure_building_loaded(controller, int(bid))
        except Exception:
            pass
        ob = world_svc.find_building_entity_by_id(controller, int(bid))
        if ob is None:
            return
    # Tag basic spawner info
    try:
        setattr(ob, '_is_spawner_visual', True)
    except Exception:
        pass
    try:
        inst = controller.parent.model.selected_instance or {}
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

    # Link back to ECS spawner entity id if present
    try:
        world = world_svc.get_world(controller)
        comps = getattr(world, 'components', {}) if world else {}
        if world and 'SpawnerConfig' in comps:
            for eid in world.get_entities_with('SpawnerConfig'):
                try:
                    cfg = comps['SpawnerConfig'][eid]
                    if getattr(ob, 'spawn_id', None) == str(getattr(cfg, 'instance_id', getattr(cfg, 'template_id', ''))):
                        setattr(ob, '_spawner_eid', eid)
                        setattr(ob, '_world_ref', world)
                        break
                except Exception:
                    continue
    except Exception:
        pass

    # Make editor-visible
    vis_svc.set_building_visible(controller, int(bid), True)

    # Apply per-instance visuals overrides
    _apply_scale_from_mapping(controller, ob, state_key)

    # Quick camera centering to revealed building
    try:
        cam = getattr(controller.parent.game, 'camera', None)
        if cam is not None:
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


def tag_building_for_state(controller, bid: int, state_key: str, *, visible: bool = True, center: bool = False) -> None:
    ob = world_svc.find_building_entity_by_id(controller, int(bid))
    if ob is None:
        try:
            loader_svc.ensure_building_loaded(controller, int(bid))
        except Exception:
            pass
        ob = world_svc.find_building_entity_by_id(controller, int(bid))
        if ob is None:
            return
    # Basic tags
    try:
        setattr(ob, '_is_spawner_visual', True)
    except Exception:
        pass
    try:
        inst = controller.parent.model.selected_instance or {}
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

    # Link to spawner eid if available
    try:
        world = world_svc.get_world(controller)
        comps = getattr(world, 'components', {}) if world else {}
        if world and 'SpawnerConfig' in comps:
            for eid in world.get_entities_with('SpawnerConfig'):
                try:
                    cfg = comps['SpawnerConfig'][eid]
                    if getattr(ob, 'spawn_id', None) == str(getattr(cfg, 'instance_id', getattr(cfg, 'template_id', ''))):
                        setattr(ob, '_spawner_eid', eid)
                        setattr(ob, '_world_ref', world)
                        break
                except Exception:
                    continue
    except Exception:
        pass

    # Editor visibility only
    try:
        vis_svc.set_building_visible(controller, int(bid), bool(visible))
    except Exception:
        pass

    # Position the building relative to the spawner's anchor immediately using visuals offset
    try:
        if not bool(getattr(ob, '_spawner_visual_dragging', False)):
            cx, cy, zone = _compute_anchor_and_offset(controller, state_key)
            try:
                if getattr(ob, 'zone', None) != zone:
                    setattr(ob, 'zone', zone)
            except Exception:
                pass
            try:
                setattr(ob, 'rel_x', int(cx))
                setattr(ob, 'rel_y', int(cy))
            except Exception:
                pass
            _apply_scale_from_mapping(controller, ob, state_key)
            _apply_split_ratio_from_mapping(controller, ob, state_key)
            # Persist placement
            try:
                arr = load_buildings_instances()
                changed = False
                for ee in arr:
                    try:
                        if int(ee.get('id')) == int(bid):
                            if str(ee.get('zone') or 'lobby') != str(zone):
                                ee['zone'] = str(zone)
                                changed = True
                            if int(ee.get('rel_x') or 0) != int(cx):
                                ee['rel_x'] = int(cx)
                                changed = True
                            if int(ee.get('rel_y') or 0) != int(cy):
                                ee['rel_y'] = int(cy)
                                changed = True
                            break
                    except Exception:
                        continue
                if changed:
                    write_buildings_instances(arr)
            except Exception:
                pass
    except Exception:
        pass

    if center:
        try:
            cam = getattr(controller.parent.game, 'camera', None)
            if cam is not None:
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
