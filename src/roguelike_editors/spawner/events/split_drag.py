from __future__ import annotations

from typing import Any, Optional
import pygame
import logging

from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)
from roguelike_editors.spawner.services.persistence import (
    load_instances_json as sp_load_instances,
    write_instances_json as sp_write_instances,
)
from .utils import find_building_in_world_by_id, log_info_safe
from .types import EditorCtx

logger = logging.getLogger(__name__)

def propagate_split_ratio(ctx: EditorCtx, source_ob: Any, ratio: float, ip: Any) -> None:
    """Per-visual-instance behavior: do not propagate split_ratio to other buildings.

    Previously we mirrored the split ratio across all buildings linked to the same spawner
    (or sharing zone/rel). Now each visual state/instance has its own independent split.
    This function remains as a no-op to keep call sites intact.
    """
    return None


def begin_split_drag(ctx: EditorCtx, bid: int, event: pygame.event.Event) -> bool:
    model = ctx.model
    ip = getattr(ctx.controller, 'instance_properties', None)
    try:
        model.split_drag_active = True
        model.split_drag_bid = int(bid)
        setattr(ctx.controller.events, '_split_drag_first_logged', False)
    except (AttributeError, TypeError, ValueError):
        logger.debug("begin_split_drag: failed to set split drag flags", exc_info=True)
        model.split_drag_active = False
        model.split_drag_bid = None
    if not getattr(model, 'split_drag_active', False):
        return False
    # Resolve building entity in visuals or world
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(bid))
    except (AttributeError, TypeError, ValueError):
        logger.debug("begin_split_drag: failed to resolve building from visuals", exc_info=True)
        ob = None
    if ob is None:
        ob = find_building_in_world_by_id(ctx.world, int(bid))
    if ob is None or ctx.split_tool is None:
        return False
    try:
        ctx.split_adapter.selected_building = ob
        ctx.split_adapter.split_dragging = True
        ctx.split_tool.start_drag(ob)
        # Cache propagation key (zone, rel_x, rel_y)
        try:
            oz = getattr(getattr(ob, 'model', ob), 'zone', None) or 'lobby'
            orx = int(getattr(getattr(ob, 'model', ob), 'rel_x', 0))
            ory = int(getattr(getattr(ob, 'model', ob), 'rel_y', 0))
            setattr(model, '_split_propagation_key', (str(oz), int(orx), int(ory)))
        except (AttributeError, TypeError, ValueError):
            logger.debug("begin_split_drag: failed to set _split_propagation_key", exc_info=True)
            setattr(model, '_split_propagation_key', None)
        # Log start
        try:
            mx, my = event.pos
        except (AttributeError, TypeError, ValueError):
            logger.debug("begin_split_drag: failed to read event.pos", exc_info=True)
            mx = my = 0
        try:
            sr = getattr(ob, 'split_ratio', None)
            log_info_safe(ctx.logger, "SpawnerEditor: split drag START bid=%s ratio=%s mouse=(%s,%s)", str(bid), f"{sr:.3f}" if isinstance(sr, (int, float)) else str(sr), int(mx), int(my))
        except (AttributeError, TypeError, ValueError):
            logger.debug("begin_split_drag: failed to start split tool drag or log start", exc_info=True)
    except (AttributeError, TypeError, ValueError):
        logger.debug("begin_split_drag: unexpected error initializing drag", exc_info=True)
        return False
    # Suppress gameplay input during split drag
    try:
        if hasattr(ctx.world, 'state'):
            setattr(ctx.world.state, 'spawner_input_suppressed', True)
    except AttributeError:
        logger.debug("begin_split_drag: failed to suppress gameplay input", exc_info=True)
    return True


def update_split_drag(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    ip = getattr(ctx.controller, 'instance_properties', None)
    cam = ctx.camera
    model = ctx.model
    try:
        bid = int(model.split_drag_bid)
    except (TypeError, ValueError):
        return False
    # Resolve ob
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(bid))
    except (AttributeError, TypeError, ValueError):
        ob = None
    if ob is None:
        ob = find_building_in_world_by_id(ctx.world, int(bid))
    if ob is None or cam is None or getattr(ob, 'image', None) is None:
        return False
    try:
        if ctx.split_tool is not None:
            ctx.split_adapter.selected_building = ob
            ctx.split_adapter.split_dragging = True
            ctx.split_tool.update_drag(event.pos, cam)
            # Mirror to world entity if different
            try:
                world_ob = find_building_in_world_by_id(ctx.world, int(bid))
            except (AttributeError, TypeError, ValueError):
                world_ob = None
            if world_ob is not None and world_ob is not ob:
                try:
                    world_ob.split_ratio = float(getattr(ob, 'split_ratio', getattr(world_ob, 'split_ratio', 0.5)))
                except (AttributeError, TypeError, ValueError):
                    pass
                try:
                    mh = int(getattr(getattr(world_ob, 'model', None), 'image', getattr(world_ob, 'image', None)).get_height())
                    setattr(world_ob.model, '_cut_world', int(mh * float(world_ob.split_ratio)))
                except (AttributeError, TypeError, ValueError):
                    pass
                try:
                    if hasattr(world_ob, 'controller') and world_ob.controller:
                        world_ob.controller.update_on_camera_change()
                except AttributeError:
                    pass
        # One-time MOTION sample
        if not getattr(ctx.controller.events, '_split_drag_first_logged', False):
            try:
                src = 'visuals' if (ip is not None and hasattr(ip, 'visuals') and ip.visuals._find_building_entity_by_id(int(bid)) is ob) else 'world'
            except (AttributeError, TypeError, ValueError):
                src = 'unknown'
            try:
                bx, by = cam.apply((getattr(ob, 'x', 0), getattr(ob, 'y', 0)))
                _, h_scaled = cam.scale(ob.image.get_size())
            except AttributeError:
                bx = by = h_scaled = None
            try:
                mx, my = event.pos
            except (AttributeError, TypeError, ValueError):
                mx = my = None
            try:
                ratio_now = float(getattr(ob, 'split_ratio', 0.0))
            except (TypeError, ValueError):
                ratio_now = None
            log_info_safe(
                ctx.logger,
                "SpawnerEditor: split drag MOTION1 bid=%s src=%s bx=%s by=%s h_scaled=%s mouse=(%s,%s) ratio=%s",
                str(bid), src, str(bx), str(by), str(h_scaled), str(mx), str(my),
                f"{ratio_now:.3f}" if isinstance(ratio_now, (int, float)) else str(ratio_now),
            )
            setattr(ctx.controller.events, '_split_drag_first_logged', True)
        # Propagate live ratio
        try:
            cur_r = float(getattr(ob, 'split_ratio', 0.5))
            propagate_split_ratio(ctx, ob, cur_r, ip)
        except (AttributeError, TypeError, ValueError):
            pass
        return True
    except (AttributeError, TypeError, ValueError):
        return False


def end_split_drag(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    model = ctx.model
    bid = getattr(model, 'split_drag_bid', None)
    model.split_drag_active = False
    model.split_drag_bid = None
    # Stop tool
    try:
        if ctx.split_tool is not None:
            ctx.split_tool.stop_drag()
        if ctx.split_adapter is not None:
            ctx.split_adapter.split_dragging = False
            ctx.split_adapter.selected_building = None
    except AttributeError:
        pass
    # Re-enable gameplay input
    try:
        if ctx.world is not None and hasattr(ctx.world, 'state'):
            setattr(ctx.world.state, 'spawner_input_suppressed', False)
    except AttributeError:
        pass
    if bid is not None:
        try:
            ip = getattr(ctx.controller, 'instance_properties', None)
            ob = None
            try:
                if ip is not None and hasattr(ip, 'visuals'):
                    ob = ip.visuals._find_building_entity_by_id(int(bid))
            except (AttributeError, TypeError, ValueError):
                ob = None
            if ob is None:
                ob = find_building_in_world_by_id(ctx.world, int(bid))
            cur_ratio = float(getattr(ob, 'split_ratio', 0.5)) if ob is not None else None
        except (AttributeError, TypeError, ValueError):
            logger.debug("end_split_drag: failed to compute current split ratio", exc_info=True)
            cur_ratio = None
        # Log end
        log_info_safe(ctx.logger, "SpawnerEditor: split drag END bid=%s button=%s ratio=%s", str(bid), str(getattr(event, 'button', None)), f"{cur_ratio:.3f}" if isinstance(cur_ratio, (int, float)) else str(cur_ratio))
        if cur_ratio is not None:
            # Persist per-visual split_ratio inside spawners_instances.json for the selected spawner instance
            try:
                sel_inst = getattr(getattr(getattr(ctx.controller, 'instance_properties', None), 'model', None), 'selected_instance', None)
            except AttributeError:
                sel_inst = None
            try:
                if isinstance(sel_inst, dict) and sel_inst.get('id') is not None:
                    arr = sp_load_instances()
                    target_id = str(sel_inst.get('id'))
                    updated = False
                    for inst in arr or []:
                        try:
                            if str(inst.get('id')) != target_id:
                                continue
                            vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                            for k, v in list(vis.items()):
                                try:
                                    if isinstance(v, dict):
                                        vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                    else:
                                        vid = int(v)
                                except (TypeError, ValueError):
                                    vid = None
                                if vid is not None and int(vid) == int(bid):
                                    if not isinstance(v, dict):
                                        entry = {'instance_id': int(vid), 'template_id': v if not isinstance(v, dict) else v.get('template_id')}
                                        entry['split_ratio'] = round(float(cur_ratio), 3)
                                        vis[k] = entry
                                    else:
                                        vv = dict(v)
                                        vv['split_ratio'] = round(float(cur_ratio), 3)
                                        vis[k] = vv
                                    inst['visuals'] = vis
                                    updated = True
                                    break
                            if updated:
                                break
                        except Exception:
                            continue
                    if updated:
                        try:
                            sp_write_instances(arr)
                        except OSError:
                            logger.debug("end_split_drag: failed to persist spawners_instances split_ratio", exc_info=True)
            except Exception:
                logger.debug("end_split_drag: error updating spawners_instances split_ratio", exc_info=True)
            # Cleanup: remove legacy overrides.split_ratio from buildings_instances.json for this building id
            try:
                data = svc_load_buildings_instances()
            except OSError:
                data = []
            cleaned = False
            for e in data or []:
                try:
                    if int(e.get('id')) != int(bid):
                        continue
                except Exception:
                    continue
                ov = e.get('overrides') or {}
                if isinstance(ov, dict) and 'split_ratio' in ov:
                    try:
                        ov.pop('split_ratio', None)
                    except Exception:
                        pass
                    # remove empty overrides dict to keep file tidy
                    if not ov:
                        try:
                            e.pop('overrides', None)
                        except Exception:
                            e['overrides'] = {}
                    else:
                        e['overrides'] = ov
                    cleaned = True
                break
            if cleaned:
                try:
                    svc_write_buildings_instances(data)
                except OSError:
                    logger.debug("end_split_drag: failed to persist cleanup on buildings_instances", exc_info=True)
            # Propagate end-state
            try:
                propagate_split_ratio(ctx, ob, float(cur_ratio), ip)
            except (AttributeError, TypeError, ValueError):
                logger.debug("end_split_drag: failed to propagate end-state split ratio", exc_info=True)
    # Reset flags
    try:
        setattr(ctx.controller.events, '_split_drag_first_logged', False)
    except AttributeError:
        pass
    try:
        setattr(model, '_split_propagation_key', None)
    except AttributeError:
        pass
    return True
