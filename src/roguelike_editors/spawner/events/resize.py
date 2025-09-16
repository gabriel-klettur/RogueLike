from __future__ import annotations

from typing import Any
import pygame
import logging

from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)
from roguelike_editors.spawner.services.persistence import (
    load_instances_json as sp_load_instances_json,
    write_instances_json as sp_write_instances_json,
)
from .types import EditorCtx

logger = logging.getLogger(__name__)

def start_resize(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Begin resize mode for the currently selected building.
    Records origin and initial size on the model, suppresses gameplay input.
    """
    model = ctx.model
    ip = getattr(ctx.controller, 'instance_properties', None)
    sel_bid = None
    try:
        # IMPORTANT: selection lives in InstanceProperties.visuals.model, not in InstanceProperties.model.visuals
        vmodel = getattr(getattr(ip, 'visuals', None), 'model', None) if ip else None
        sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
    except (AttributeError, TypeError):
        sel_bid = None
    if sel_bid is None:
        return False
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
    except (AttributeError, ValueError, TypeError):
        logger.debug("start_resize: failed to resolve building from visuals", exc_info=True)
        ob = None
    if ob is None:
        return False
    try:
        mx, my = event.pos
    except (AttributeError, TypeError, ValueError):
        logger.debug("start_resize: failed to read event.pos", exc_info=True)
        mx = my = 0
    try:
        w0, h0 = ob.image.get_size()
    except AttributeError:
        logger.debug("start_resize: failed to get original image size", exc_info=True)
        return False
    # Set resize flags and context
    try:
        model.resizing_visual = True
        model.resizing_visual_bid = int(sel_bid)
        model.resize_origin = (int(mx), int(my))
        model.initial_size = (int(w0), int(h0))
        if hasattr(ctx.world, 'state'):
            setattr(ctx.world.state, 'spawner_input_suppressed', True)
        try:
            logger.debug(f"[resize] start sel_bid={sel_bid} origin=({mx},{my}) initial=({w0},{h0})")
        except Exception:
            pass
    except (AttributeError, TypeError, ValueError):
        logger.debug("start_resize: failed to set resize flags or suppress input", exc_info=True)
    return True


def update_resize_motion(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Update size while in resize mode, emulating Building Editor logic."""
    model = ctx.model
    if not getattr(model, 'resizing_visual', False):
        return False
    bid = getattr(model, 'resizing_visual_bid', None)
    if bid is None:
        return False
    ip = getattr(ctx.controller, 'instance_properties', None)
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(bid))
    except (AttributeError, ValueError, TypeError):
        logger.debug("update_resize_motion: failed to resolve building from visuals", exc_info=True)
        ob = None
    if ob is None:
        return False
    try:
        mx, my = event.pos
    except (AttributeError, TypeError, ValueError):
        logger.debug("update_resize_motion: failed to read event.pos", exc_info=True)
        return False
    start = getattr(model, 'resize_origin', None) or (mx, my)
    w0, h0 = getattr(model, 'initial_size', (None, None))
    if w0 is None or h0 is None:
        try:
            w0, h0 = ob.image.get_size()
        except AttributeError:
            logger.debug("update_resize_motion: failed to get initial image size", exc_info=True)
            return False
    dx = int(mx) - int(start[0])
    dy = int(my) - int(start[1])
    delta = max(dx, dy)
    aspect = (w0 / h0) if h0 else 1.0
    new_w = max(50, int(w0 + delta))
    new_h = max(50, int(new_w / aspect))
    try:
        cur_size = ob.image.get_size()
    except AttributeError:
        logger.debug("update_resize_motion: failed to get current image size", exc_info=True)
        cur_size = None
    try:
        ob.resize(int(new_w), int(new_h))
        try:
            logger.debug(f"[resize] motion bid={bid} new_size=({new_w},{new_h}) from delta=({dx},{dy})")
        except Exception:
            pass
    except (AttributeError, TypeError, ValueError):
        logger.debug("update_resize_motion: failed to resize object", exc_info=True)
        return False
    # Pulse feedback similar to Building Editor when size changes
    try:
        if cur_size is not None and (int(new_w), int(new_h)) != cur_size:
            setattr(ctx.controller.model, 'tutorial_resized_pulse', True)
    except AttributeError:
        logger.debug("update_resize_motion: failed to set tutorial_resized_pulse", exc_info=True)
    return True


def finish_resize(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Persist size overrides on LMB up after resizing a visual building."""
    model = ctx.model
    world = ctx.world
    model.resizing_visual = False
    bid = getattr(model, 'resizing_visual_bid', None)
    model.resizing_visual_bid = None
    try:
        if hasattr(world, 'state'):
            setattr(world.state, 'spawner_input_suppressed', False)
    except AttributeError:
        logger.debug("finish_resize: failed to clear input suppression", exc_info=True)
    if bid is None:
        return True
    # Read current size from world entity
    try:
        ip = getattr(ctx.controller, 'instance_properties', None)
        ob = ip.visuals._find_building_entity_by_id(int(bid))
        cur_w, cur_h = ob.image.get_size()
    except (AttributeError, TypeError, ValueError):
        logger.debug("finish_resize: failed to resolve object or current size", exc_info=True)
        cur_w = cur_h = None

    # 1) Persist to buildings_instances.json (global per-building), for backward compatibility
    try:
        data = svc_load_buildings_instances()
    except OSError:
        logger.debug("finish_resize: failed to load buildings_instances", exc_info=True)
        data = []
    changed_bi = False
    if cur_w is not None and cur_h is not None:
        for e in data or []:
            try:
                if int(e.get('id')) != int(bid):
                    continue
            except (ValueError, TypeError):
                continue
            ov = e.get('overrides') or {}
            if not isinstance(ov, dict):
                ov = {}
            ov['scale'] = [int(cur_w), int(cur_h)]
            e['overrides'] = ov
            changed_bi = True
            break
    if changed_bi:
        try:
            svc_write_buildings_instances(data)
        except OSError:
            logger.debug("finish_resize: failed to persist buildings_instances after resize", exc_info=True)

    # 2) Persist to spawners_instances.json inside the selected instance's visuals mapping
    try:
        inst_data = sp_load_instances_json()
    except OSError:
        logger.debug("finish_resize: failed to load spawners_instances.json", exc_info=True)
        inst_data = []
    changed_sp = False
    sel_inst = None
    try:
        sel_inst = getattr(getattr(ctx.controller.instance_properties, 'model', None), 'selected_instance', None)
    except Exception:
        sel_inst = None
    target_id = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
    if target_id is not None and cur_w is not None and cur_h is not None:
        for inst in inst_data or []:
            try:
                if str(inst.get('id')) != target_id:
                    continue
                vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                # find mapping entry referencing this building id and set scale
                for k, v in list(vis.items()):
                    try:
                        if isinstance(v, dict):
                            vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                        else:
                            vid = int(v)
                    except Exception:
                        vid = None
                    if vid is not None and int(vid) == int(bid):
                        if not isinstance(v, dict):
                            v = {'instance_id': int(vid)}
                        v['scale'] = [int(cur_w), int(cur_h)]
                        vis[k] = v
                        inst['visuals'] = vis
                        changed_sp = True
                        break
                break
            except Exception:
                continue
    if changed_sp:
        try:
            sp_write_instances_json(inst_data)
        except OSError:
            logger.debug("finish_resize: failed to persist spawners_instances.json after resize", exc_info=True)

    # 3) Update in-memory mapping on the selected instance model (so UI/state is in sync)
    try:
        ipc = getattr(ctx.controller, 'instance_properties', None)
        if ipc is not None and isinstance(getattr(ipc.model, 'visuals', None), dict):
            vis_map = dict(ipc.model.visuals)
            for k, v in list(vis_map.items()):
                try:
                    if isinstance(v, dict):
                        vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                    else:
                        vid = int(v)
                except Exception:
                    vid = None
                if vid is not None and int(vid) == int(bid):
                    if not isinstance(v, dict):
                        v = {'instance_id': int(vid)}
                    v['scale'] = [int(cur_w), int(cur_h)] if (cur_w is not None and cur_h is not None) else v.get('scale')
                    vis_map[k] = v
                    ipc.model.visuals = vis_map
                    if isinstance(ipc.model.selected_instance, dict):
                        ipc.model.selected_instance['visuals'] = vis_map
                    break
    except Exception:
        logger.debug("finish_resize: failed to update in-memory visuals mapping with scale", exc_info=True)

    return True
