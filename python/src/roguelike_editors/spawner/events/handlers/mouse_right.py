from __future__ import annotations

import logging
import pygame

from .. import split_drag as split
from .. import resize as rz
from ...services.picking import pick_spawner_under_cursor
from ...services import zone_for_global_tile
from ...services.persistence import find_instance_in_json, persist_drop, load_instances_json, write_instances_json
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)


def handle_mousebuttonup(h, ctx, event: pygame.event.Event) -> bool:
    """Handle MOUSEBUTTONUP for split end, visual move end, anchor drag end, and resize finish."""
    logger = logging.getLogger(__name__)
    world = ctx.world
    model = h.model

    # Split drag END on any mouse button up
    if event.type == pygame.MOUSEBUTTONUP and getattr(model, 'split_drag_active', False):
        try:
            if split.end_split_drag(ctx, event):
                return True
        except Exception:
            logger.debug("mousebuttonup: split.end_split_drag failed", exc_info=True)

    # RMB up: finish moving a visual building (persist offset and building rel position)
    if event.type == pygame.MOUSEBUTTONUP and event.button == 3 and getattr(model, 'moving_visual', False):
        bid = getattr(model, 'moving_visual_bid', None)
        model.moving_visual = False
        try:
            ip = getattr(h.controller, 'instance_properties', None)
            ob = None
            if bid is not None and ip is not None and hasattr(ip, 'visuals'):
                try:
                    ob = ip.visuals._find_building_entity_by_id(int(bid))
                except Exception:
                    ob = None
            # Clear drag guard on the world object
            try:
                if ob is not None:
                    setattr(ob, '_spawner_visual_dragging', False)
            except Exception:
                pass
            # Persist offset in spawners_instances.json (relative to spawner center)
            try:
                sel_inst = getattr(getattr(h.controller.instance_properties, 'model', None), 'selected_instance', None)
            except Exception:
                sel_inst = None
            if ob is not None and sel_inst is not None and isinstance(sel_inst, dict):
                try:
                    # Resolve spawner EID for this building (tagged during runtime sync)
                    sp_eid = getattr(ob, '_spawner_eid', None)
                    cfg = world.components['SpawnerConfig'][sp_eid] if sp_eid is not None else None
                    zone = getattr(cfg, 'zone', None) or getattr(ob, 'zone', None) or 'lobby'
                    off_x, off_y = (0, 0)
                    try:
                        from roguelike_engine.config.map_config import global_map_settings as _gms
                        off_x, off_y = _gms.zone_offsets.get(str(zone), (0, 0))
                    except Exception:
                        off_x, off_y = (0, 0)
                    # Anchor center in zone-relative px
                    ax, ay = (0, 0)
                    try:
                        tx, ty = cfg.anchor_tile
                        ax = int((int(tx) - int(off_x)) * TILE_SIZE + TILE_SIZE // 2)
                        ay = int((int(ty) - int(off_y)) * TILE_SIZE + TILE_SIZE // 2)
                    except Exception:
                        # Fallback: compute from selected instance tile
                        try:
                            t = sel_inst.get('tile', [0, 0])
                            ax = int(int(t[0]) * TILE_SIZE + TILE_SIZE // 2)
                            ay = int(int(t[1]) * TILE_SIZE + TILE_SIZE // 2)
                        except Exception:
                            ax, ay = (0, 0)
                    # Compute offset = building.rel - anchor_center (zone-relative px)
                    dx = int(getattr(ob, 'rel_x', getattr(getattr(ob, 'model', ob), 'rel_x', 0)) or 0) - ax
                    dy = int(getattr(ob, 'rel_y', getattr(getattr(ob, 'model', ob), 'rel_y', 0)) or 0) - ay
                    # Update visuals mapping entry that points to this building id
                    arr = load_instances_json()
                    # Identify instance on disk by id
                    cur_id = str(sel_inst.get('id')) if sel_inst.get('id') is not None else None
                    for inst in arr:
                        try:
                            if str(inst.get('id')) != cur_id:
                                continue
                            vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                            changed = False
                            for k, v in vis.items():
                                try:
                                    vid = None
                                    if isinstance(v, dict):
                                        vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                    else:
                                        vid = int(v)
                                except Exception:
                                    vid = None
                                if vid is not None and int(vid) == int(bid):
                                    # ensure dict form and write/remove offset as needed
                                    if not isinstance(v, dict):
                                        entry = {'instance_id': vid, 'template_id': None}
                                        if int(dx) != 0 or int(dy) != 0:
                                            entry['offset'] = [int(dx), int(dy)]  # type: ignore[index]
                                        vis[k] = entry
                                    else:
                                        vv = dict(v)
                                        if int(dx) != 0 or int(dy) != 0:
                                            vv['offset'] = [int(dx), int(dy)]
                                        else:
                                            try:
                                                vv.pop('offset', None)
                                            except Exception:
                                                pass
                                        vis[k] = vv
                                    inst['visuals'] = vis
                                    changed = True
                                    break
                            if changed:
                                write_instances_json(arr)
                                try:
                                    if cfg is not None:
                                        if getattr(cfg, 'visuals_offsets_px', None) is None:
                                            cfg.visuals_offsets_px = {}
                                        key_l = str(k).strip().lower()
                                        cfg.visuals_offsets_px[key_l] = (int(dx), int(dy))
                                except Exception:
                                    pass
                                break
                        except Exception:
                            continue
                    # Persist buildings_instances rel_x/rel_y for this building id
                    try:
                        data = svc_load_buildings_instances()
                    except OSError:
                        data = []
                    changed2 = False
                    for e in data or []:
                        try:
                            if int(e.get('id')) != int(bid):
                                continue
                        except Exception:
                            continue
                        try:
                            e['rel_x'] = int(getattr(ob, 'rel_x', getattr(getattr(ob, 'model', ob), 'rel_x', 0)) or 0)
                            e['rel_y'] = int(getattr(ob, 'rel_y', getattr(getattr(ob, 'model', ob), 'rel_y', 0)) or 0)
                            e['zone'] = str(zone)
                            changed2 = True
                        except Exception:
                            pass
                        break
                    if changed2:
                        try:
                            svc_write_buildings_instances(data)
                        except OSError:
                            pass
                except Exception:
                    logger.debug("RMB up: visual persist failed", exc_info=True)
            # Clear world input suppression now that move finished
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
            # Clear delta cache
            h._moving_visual_delta_world = None
        except Exception:
            logger.debug("RMB up: finalize moving_visual failed", exc_info=True)
        return True

    # RMB up: stop anchor drag if active and persist movement (or ask for zone confirm)
    if event.type == pygame.MOUSEBUTTONUP and event.button == 3 and getattr(model, 'dragging', False):
        eid = getattr(model, 'dragging_eid', None)
        model.dragging = False
        model.dragging_eid = None
        try:
            if isinstance(eid, int) and eid in world.components.get('SpawnerConfig', {}):
                cfg = world.components['SpawnerConfig'][eid]
                tx, ty = cfg.anchor_tile
                proposed_zone = zone_for_global_tile(int(tx), int(ty))
                # Snapshot captured at drag start
                snapshot = getattr(h, '_drag_start_entry', None) or {}
                orig_zone = snapshot.get('zone') or getattr(cfg, 'zone', None)
                orig_local = snapshot.get('local_tile') or snapshot.get('orig_local')
                if proposed_zone and str(proposed_zone) != str(orig_zone):
                    try:
                        model.pending_zone_confirm = {
                            'eid': eid,
                            'orig_zone': orig_zone,
                            'proposed_zone': proposed_zone,
                            'orig_local': orig_local,
                        }
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_input_suppressed', True)
                    except Exception:
                        pass
                else:
                    try:
                        persist_drop(world, eid, snapshot, orig_zone=orig_zone)
                    except Exception:
                        logger.debug("RMB up: persist_drop failed", exc_info=True)
                    try:
                        h.controller.spawner_instances.refresh_from_disk()
                    except Exception:
                        pass
                    try:
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_input_suppressed', False)
                    except Exception:
                        pass
                    h._drag_start_entry = None
        except Exception:
            logger.debug("RMB up: error finalizing anchor drag", exc_info=True)
        return True

    # LMB up: finish resize
    if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and getattr(model, 'resizing_visual', False):
        try:
            if rz.finish_resize(ctx, event):
                return True
        except Exception:
            logger.debug("LMB up: rz.finish_resize failed", exc_info=True)

    return False


def handle_mousedown_right(h, ctx, event: pygame.event.Event) -> bool:
    """Handle MOUSEBUTTONDOWN button==3 branches: spawner selection/drag, moving visuals, split drag, anchor drag from selected building."""
    logger = logging.getLogger(__name__)
    world, camera = ctx.world, ctx.camera
    try:
        mx, my = event.pos
        eid = pick_spawner_under_cursor(world, camera, int(mx), int(my))
        if eid is not None:
            try:
                h.model.selected_eid = eid
            except Exception:
                pass
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_selected_eid', eid)
            except Exception:
                pass
            # Clear building selection when selecting a spawner with RMB too
            try:
                ip = getattr(h.controller, 'instance_properties', None)
                if ip is not None and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                    ip.visuals.model.selected_building_id = None
            except Exception:
                pass
            # Begin anchor drag on spawner center (RMB)
            try:
                h.model.dragging = True
                h.model.dragging_eid = eid
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', True)
            except Exception:
                logger.debug("handle_event: failed to start spawner anchor drag", exc_info=True)
            # Capture snapshot for persistence at drop
            try:
                cfg = world.components['SpawnerConfig'][eid]
                zone = getattr(cfg, 'zone', None)
                tx, ty = cfg.anchor_tile
                orig_zone = zone
                off = (0, 0)
                try:
                    from roguelike_engine.config.map_config import global_map_settings as _gms
                    off = _gms.zone_offsets.get(zone, (0, 0)) if zone else (0, 0)
                except Exception:
                    off = (0, 0)
                local = (int(tx - off[0]), int(ty - off[1]))
                inst_list, idx_found, overrides = find_instance_in_json(str(getattr(cfg, 'template_id', '')), str(zone), tuple(local))
                inst_id = None
                try:
                    if idx_found is not None:
                        inst_id = inst_list[idx_found].get('id')
                except Exception:
                    inst_id = None
                h._drag_start_entry = {
                    'id': inst_id,
                    'zone': zone,
                    'orig_zone': orig_zone,
                    'local_tile': local,
                    'orig_local': local,
                    'overrides': overrides if isinstance(overrides, dict) else None,
                }
            except Exception:
                logger.debug("handle_event: failed to capture drag snapshot", exc_info=True)
            return True
    except Exception:
        logger.debug("handle_event: spawner anchor RMB selection failed", exc_info=True)
    # Not clicking a spawner anchor: clear spawner selection before other interactions
    try:
        if getattr(h.model, 'selected_eid', None) is not None:
            h.model.selected_eid = None
        if hasattr(world, 'state'):
            setattr(world.state, 'spawner_selected_eid', None)
    except Exception:
        pass
    if getattr(h.model, 'remove_mode_active', False) or getattr(h.model, 'placing_template_id', None):
        return False
    view = getattr(h.controller, 'view', None)
    ip = getattr(h.controller, 'instance_properties', None)
    mx, my = event.pos
    # Before split/anchor drag: begin moving a visual if clicked over it
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
        else:
            ob = None
    except Exception:
        ob = None
    if ob is not None and getattr(ip.visuals.model, 'selected_building_id', None) is not None:
        try:
            sel_bid = None
            try:
                sel_bid = int(getattr(ip.visuals.model, 'selected_building_id') or -1)
            except Exception:
                sel_bid = None
            bid = None
            try:
                bid = int(getattr(ob, 'id'))
            except Exception:
                bid = None
            if sel_bid is not None and bid is not None and int(sel_bid) == int(bid):
                hidden = False
                try:
                    hidden = bool(getattr(ob, 'editor_hidden', False))
                except Exception:
                    hidden = False
                same_instance = True
                try:
                    sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                    sel_sid = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                    ob_sid = str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', '')))
                    if sel_sid is not None:
                        same_instance = (ob_sid == sel_sid)
                except Exception:
                    same_instance = True
                if (not hidden) and same_instance:
                    # Begin move
                    h.model.moving_visual = True
                    h.model.moving_visual_bid = bid
                    try:
                        setattr(ob, '_spawner_visual_dragging', True)
                    except Exception:
                        pass
                    try:
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_input_suppressed', True)
                    except Exception:
                        pass
                    try:
                        z = getattr(camera, 'zoom', 1.0) or 1.0
                        wx = int(mx / z + camera.offset_x)
                        wy = int(my / z + camera.offset_y)
                        h._moving_visual_delta_world = (int(ob.x) - wx, int(ob.y) - wy)
                    except Exception:
                        h._moving_visual_delta_world = (0, 0)
                    return True
        except Exception:
            logger.debug("handle_event: failed to evaluate moving visual guards", exc_info=True)
    split_rect = getattr(view, '_last_split_handle_rect', None) if view is not None else None
    # 1) Split handle: begin split drag
    if split_rect is not None and pygame.Rect(split_rect).collidepoint(mx, my):
        sel_bid = None
        try:
            vmodel = getattr(getattr(ip, 'visuals', None), 'model', None) if ip else None
            sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
        except (AttributeError, TypeError):
            sel_bid = None
        target_bid = sel_bid
        try:
            if target_bid is None and ip is not None and hasattr(ip, 'visuals'):
                ob_pick = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                if ob_pick is not None and getattr(ob_pick, 'id', None) is not None:
                    target_bid = int(getattr(ob_pick, 'id'))
                    try:
                        ip.visuals.model.selected_building_id = int(target_bid)
                    except (AttributeError, TypeError, ValueError):
                        logger.debug("handle_event: failed to set selected_building_id during split-start", exc_info=True)
        except (AttributeError, TypeError, ValueError):
            logger.debug("handle_event: error while determining target_bid for split drag", exc_info=True)
        if target_bid is not None:
            if split.begin_split_drag(ctx, int(target_bid), event):
                return True
    # 2) Otherwise: start anchor drag for currently selected building's spawner
    try:
        ip = getattr(h.controller, 'instance_properties', None)
        vmodel = getattr(getattr(ip, 'visuals', None), 'model', None) if ip else None
        sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
    except (AttributeError, TypeError):
        sel_bid = None
    if sel_bid is not None:
        world_ob = None
        try:
            world_ob = ip.visuals._find_building_entity_by_id(int(sel_bid)) if ip and hasattr(ip, 'visuals') else None
        except Exception:
            world_ob = None
        if world_ob is None:
            from ..utils import find_building_in_world_by_id
            world_ob = find_building_in_world_by_id(ctx.world, int(sel_bid))
        sp_eid = getattr(world_ob, '_spawner_eid', None) if world_ob is not None else None
        if sp_eid is not None:
            try:
                h.model.dragging = True
                h.model.dragging_eid = sp_eid
                if hasattr(ctx.world, 'state'):
                    setattr(ctx.world.state, 'spawner_input_suppressed', True)
            except AttributeError:
                logger.debug("handle_event: failed to start anchor drag (set flags)", exc_info=True)
            try:
                cfg = world.components['SpawnerConfig'][sp_eid]
                zone = getattr(cfg, 'zone', None)
                tx, ty = cfg.anchor_tile
                from roguelike_engine.config.map_config import global_map_settings as _gms
                off = _gms.zone_offsets.get(zone, (0, 0)) if zone else (0, 0)
                local = (int(tx - off[0]), int(ty - off[1]))
                inst_list, idx_found, overrides = find_instance_in_json(str(getattr(cfg, 'template_id', '')), str(zone), tuple(local))
                inst_id = None
                try:
                    if idx_found is not None:
                        inst_id = inst_list[idx_found].get('id')
                except Exception:
                    inst_id = None
                h._drag_start_entry = {
                    'id': inst_id,
                    'zone': zone,
                    'orig_zone': zone,
                    'local_tile': local,
                    'orig_local': local,
                    'overrides': overrides if isinstance(overrides, dict) else None,
                }
            except Exception:
                logger.debug("handle_event: failed to capture drag snapshot (building path)", exc_info=True)
            return True

    return False
