from __future__ import annotations

import logging
import pygame

from ...services.picking import pick_spawner_under_cursor
from .. import resize as rz
from .. import split_drag as split
from .. import types as etypes
from ..utils import compute_spawner_handle_rects, find_building_in_world_by_id
from .helpers import reset_selected_building_size
from ...services.coords import screen_to_tile
from ...services.persistence import (
    load_instances_json,
    write_instances_json,
    load_spawners_json,
    find_instance_in_json,
)
from ...services.persistence import zone_for_global_tile
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
from roguelike_game.ecs.systems.spawner.placement.loaders import load_waves
from roguelike_game.ecs.systems.spawner.placement.config_resolver import resolve_config
from roguelike_game.ecs.systems.spawner.placement.visuals import auto_repair_state_visuals


def handle_mousedown_left(h, ctx: etypes.EditorCtx, event: pygame.event.Event) -> bool:
    """Handle MOUSEBUTTONDOWN button==1 branches: building handles, early building pick, spawner anchor selection, clear selection."""
    logger = logging.getLogger(__name__)
    world, camera = ctx.world, ctx.camera
    ip = getattr(h.controller, 'instance_properties', None)
    mx, my = event.pos

    try:
        if getattr(h.model, 'placing_template_id', None) and getattr(h.model, 'skip_first_placement_click', False):
            try:
                h.model.skip_first_placement_click = False
            except Exception:
                pass
            return True
    except Exception:
        pass

    # - - - Placement Mode: place new spawner instance on LMB - - -
    # When a template was selected from the Add dropdown, we store `placing_template_id` in the editor model.
    # Convert the first world click into a persisted instance and create the ECS entity immediately.
    try:
        placing_tpl = getattr(h.model, 'placing_template_id', None)
    except Exception:
        placing_tpl = None
    if placing_tpl:
        try:
            # 1) Compute global tile from screen coords, then derive zone and zone-local tile
            tx, ty = screen_to_tile(camera, int(mx), int(my))
            zone = zone_for_global_tile(int(tx), int(ty)) or 'lobby'
            off_x, off_y = global_map_settings.zone_offsets.get(str(zone), (0, 0))
            local = (int(tx - off_x), int(ty - off_y))

            # 2) Persist new instance to spawners_instances.json
            arr = load_instances_json()
            new_entry = {
                'template_id': str(placing_tpl),
                'zone': str(zone),
                'tile': [int(local[0]), int(local[1])],
            }
            arr.append(new_entry)
            write_instances_json(arr)

            # 3) Reload to get normalized entry (with assigned id ensured by load path)
            arr2 = load_instances_json()
            _, idx_found, _ = find_instance_in_json(str(placing_tpl), str(zone), tuple(local))
            inst = arr2[idx_found] if idx_found is not None else new_entry

            # 4) Create ECS entity now using the same resolution logic as the placement system
            try:
                tpls = load_spawners_json()
                tpl = None
                for t in (tpls or []):
                    try:
                        if str(t.get('id')) == str(placing_tpl):
                            tpl = t
                            break
                    except Exception:
                        continue
                if tpl is not None and world is not None:
                    waves = load_waves()
                    cfg = resolve_config(tpl, inst, waves)
                    eid = world.create_entity()
                    world.components['SpawnerConfig'][eid] = cfg
                    world.components['SpawnerState'][eid] = SpawnerState()
                    try:
                        auto_repair_state_visuals(world, eid, cfg, inst)
                    except Exception:
                        pass
            except Exception:
                logger.debug("[SpawnerEditor] placement: failed to create ECS entity for new instance", exc_info=True)

            # 5) Exit placement/add modes, refresh instances list, and re-enable gameplay input
            try:
                h.model.placing_template_id = None
            except Exception:
                pass
            try:
                h.controller.model.add_mode_active = False
            except Exception:
                pass
            try:
                tb = getattr(h.controller, 'instance_toolbar', None)
                if tb is not None and hasattr(tb, 'model'):
                    tb.model.add_mode_active = False
                    tb.model.add_templates = []
            except Exception:
                pass
            try:
                st = getattr(h.controller, 'spawner_toolbar', None)
                if st is not None and hasattr(st, 'model'):
                    st.model.active_tool = 'spawner_instances'
            except Exception:
                pass
            try:
                h.controller.spawner_instances.refresh_from_disk()
            except Exception:
                pass
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
            try:
                pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)
            except Exception:
                pass
        except Exception:
            logger.debug("[SpawnerEditor] placement: unexpected error handling placement click", exc_info=True)
        return True

    # - - - Remove Mode: prepare delete confirmation on LMB over spawner anchor - - -
    try:
        remove_mode = bool(getattr(h.model, 'remove_mode_active', False))
    except Exception:
        remove_mode = False
    if remove_mode:
        try:
            eid = pick_spawner_under_cursor(world, camera, int(mx), int(my))
        except Exception:
            eid = None
        if eid is not None:
            try:
                cfg = world.components['SpawnerConfig'][eid]
                zone = getattr(cfg, 'zone', 'lobby')
                tx, ty = cfg.anchor_tile
                off_x, off_y = global_map_settings.zone_offsets.get(str(zone), (0, 0))
                local = (int(tx - off_x), int(ty - off_y))
                # Fill pending delete confirmation payload for overlay + confirm handler
                h.model.pending_delete_confirm = {
                    'eid': eid,
                    'template_id': str(getattr(cfg, 'template_id', '')),
                    'zone': str(zone),
                    'local_tile': local,
                }
                # Mark candidate and suppress gameplay input until confirm/cancel
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_remove_candidate_eid', eid)
                        setattr(world.state, 'spawner_input_suppressed', True)
                except Exception:
                    pass
                # Optional tutorial pulse flag
                try:
                    setattr(h.controller.model, 'tutorial_delete_pending_pulse', True)
                except Exception:
                    pass
            except Exception:
                logging.getLogger(__name__).debug("LMB remove_mode: failed to prepare delete confirmation", exc_info=True)
            return True

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
        # Split handle with LMB: begin split drag (parity with RMB)
        try:
            split_rect = getattr(view, '_last_split_handle_rect', None) if view is not None else None
        except Exception:
            split_rect = None
        if split_rect is not None and pygame.Rect(split_rect).collidepoint(mx, my):
            target_bid = sel_bid
            try:
                if target_bid is None and ip is not None and hasattr(ip, 'visuals'):
                    ob_pick = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                    if ob_pick is not None and getattr(ob_pick, 'id', None) is not None:
                        target_bid = int(getattr(ob_pick, 'id'))
                        try:
                            ip.visuals.model.selected_building_id = int(target_bid)
                        except (AttributeError, TypeError, ValueError):
                            logging.getLogger(__name__).debug("LMB split-start: failed to set selected_building_id", exc_info=True)
            except (AttributeError, TypeError, ValueError):
                logging.getLogger(__name__).debug("LMB split-start: error while determining target_bid", exc_info=True)
            if target_bid is not None:
                try:
                    if split.begin_split_drag(ctx, int(target_bid), event):
                        return True
                except Exception:
                    logging.getLogger(__name__).debug("LMB split-start: begin_split_drag failed", exc_info=True)
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
