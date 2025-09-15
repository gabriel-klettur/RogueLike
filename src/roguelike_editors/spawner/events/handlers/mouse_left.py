from __future__ import annotations

import logging
import pygame

from ...services.picking import pick_spawner_under_cursor
from .. import resize as rz
from .. import types as etypes
from ..utils import compute_spawner_handle_rects, find_building_in_world_by_id
from .helpers import reset_selected_building_size


def handle_mousedown_left(h, ctx: etypes.EditorCtx, event: pygame.event.Event) -> bool:
    """Handle MOUSEBUTTONDOWN button==1 branches: building handles, early building pick, spawner anchor selection, clear selection."""
    logger = logging.getLogger(__name__)
    world, camera = ctx.world, ctx.camera
    ip = getattr(h.controller, 'instance_properties', None)
    mx, my = event.pos

    # 0) Building overlay handles for the currently selected building (Delete/Reset/Resize)
    sel_bid = None
    try:
        vmodel = getattr(getattr(ip, 'visuals', None), 'model', None) if ip else None
        sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
    except (AttributeError, TypeError):
        sel_bid = None
    world_ob = None
    try:
        print(f"[SpawnerEditor] LMB down at ({mx},{my}); sel_bid={sel_bid}")
    except Exception:
        pass
    if sel_bid is not None:
        try:
            world_ob = ip.visuals._find_building_entity_by_id(int(sel_bid)) if ip and hasattr(ip, 'visuals') else None
        except (AttributeError, TypeError, ValueError):
            world_ob = None
        if world_ob is None:
            world_ob = find_building_in_world_by_id(ctx.world, int(sel_bid))
    try:
        print(f"[SpawnerEditor] LMB sel_bid={sel_bid} world_ob_resolved={world_ob is not None}")
    except Exception:
        pass
    # Also detect a building under cursor to allow handle clicks even if not yet selected
    ob_under = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob_under = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
    except Exception:
        ob_under = None
    if world_ob is None and ob_under is not None:
        world_ob = ob_under
    if world_ob is not None:
        view = getattr(h.controller, 'view', None)
        # Fallback to computing rects if the view cache is missing
        del_rect = getattr(view, '_last_selected_delete_rect', None) if view is not None else None
        rst_rect = getattr(view, '_last_selected_reset_rect', None) if view is not None else None
        rz_rect = getattr(view, '_last_selected_resize_rect', None) if view is not None else None
        if del_rect is None or rst_rect is None or rz_rect is None:
            rects = compute_spawner_handle_rects(ctx.camera, world_ob)
            del_rect = del_rect or rects.get('delete')
            rst_rect = rst_rect or rects.get('reset')
            rz_rect = rz_rect or rects.get('resize')
        # Default (reset size)
        if rst_rect is not None and rst_rect.collidepoint(mx, my):
            if reset_selected_building_size(h, sel_bid):
                return True
        # Resize: begin resize mode for selected building or for the building under cursor
        if rz_rect is not None and rz_rect.collidepoint(mx, my):
            if sel_bid is None and ob_under is not None:
                try:
                    hidden = bool(getattr(ob_under, 'editor_hidden', False))
                except Exception:
                    hidden = False
                same_instance = True
                try:
                    sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                    sel_sid = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                    ob_sid = str(getattr(ob_under, 'spawner_instance_id', getattr(ob_under, 'spawn_id', '')))
                    if sel_sid is not None:
                        same_instance = (ob_sid == sel_sid)
                except Exception:
                    same_instance = True
                if (not hidden) and same_instance:
                    try:
                        bid = getattr(ob_under, 'id', None)
                        if bid is not None and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                            ip.visuals.model.selected_building_id = int(bid)
                            sel_bid = int(bid)
                            print(f"[SpawnerEditor] LMB autoselected building on resize click: bid={bid}")
                    except Exception:
                        pass
            started = False
            try:
                started = bool(rz.start_resize(ctx, event))
            except Exception:
                started = False
            try:
                print(f"[SpawnerEditor] LMB start resize: sel_bid={sel_bid} started={started}")
            except Exception:
                pass
            if started:
                return True
        # Remove: delete the selected building instance (parity with Building Editor)
        if del_rect is not None and del_rect.collidepoint(mx, my):
            try:
                from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
                    load_buildings_instances as _load_bi,
                    write_buildings_instances as _write_bi,
                )
                data = _load_bi()
                changed = False
                out = []
                for e in data or []:
                    try:
                        if int(e.get('id')) == int(sel_bid):
                            changed = True
                            continue
                    except (TypeError, ValueError):
                        pass
                    out.append(e)
                if changed:
                    _write_bi(out)
                # 2) Remove any visuals refs in spawners_instances.json pointing to this building id
                try:
                    from roguelike_editors.spawner.services.persistence import remove_visual_refs_by_building_id as _rm_vis
                    _rm_vis(int(sel_bid))
                except (ImportError, OSError, TypeError, ValueError):
                    logger.debug("handle_event: failed to remove visuals refs for building id", exc_info=True)
                # 3) Remove from live world/entities using existing helper
                try:
                    if ip and hasattr(ip, 'visuals') and hasattr(ip.visuals, '_remove_building_entity_by_id'):
                        ip.visuals._remove_building_entity_by_id(int(sel_bid))
                except (AttributeError, TypeError, ValueError):
                    logger.debug("handle_event: failed to remove building entity from live world", exc_info=True)
                # 4) Clear selection
                try:
                    if ip and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                        ip.visuals.model.selected_building_id = None
                except AttributeError:
                    logger.debug("handle_event: failed to clear selected_building_id", exc_info=True)
                return True
            except (AttributeError, OSError, TypeError, ValueError):
                logger.debug("handle_event: delete selected building flow failed", exc_info=True)

    # 0b) If no building selected yet, prioritize selecting a building under cursor before spawner anchor
    if sel_bid is None and ip is not None and hasattr(ip, 'visuals'):
        try:
            ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
        except Exception:
            ob = None
        if ob is not None:
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
                try:
                    bid = getattr(ob, 'id', None)
                    if bid is not None:
                        ip.visuals.model.selected_building_id = int(bid)
                        print(f"[SpawnerEditor] LMB selected building via early-pick: bid={bid}")
                        return True
                except Exception:
                    pass

    # 1) Spawner anchor selection (only if not clicking a handle)
    try:
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
            try:
                if ip is not None and hasattr(ip, 'visuals') and hasattr(ip.visuals, 'model'):
                    ip.visuals.model.selected_building_id = None
            except Exception:
                pass
            return True
    except Exception:
        logger.debug("handle_event: spawner anchor selection failed", exc_info=True)

    # If not clicking a spawner anchor, clear spawner selection (lose focus)
    try:
        if getattr(h.model, 'selected_eid', None) is not None:
            h.model.selected_eid = None
        if hasattr(world, 'state'):
            setattr(world.state, 'spawner_selected_eid', None)
    except Exception:
        pass

    # Else: selection under cursor (LMB-only selection)
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
            if ob is not None:
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
                    bid = getattr(ob, 'id', None)
                    if bid is not None:
                        ip.visuals.model.selected_building_id = int(bid)
                        return True
            else:
                try:
                    ip.visuals.model.selected_building_id = None
                except Exception:
                    pass
    except (AttributeError, TypeError, ValueError):
        logger.debug("handle_event: failed picking building under cursor for selection", exc_info=True)

    return False
