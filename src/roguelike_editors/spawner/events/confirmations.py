from __future__ import annotations

import pygame
from typing import Optional
import logging

from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services import (
    persist_drop,
    find_instance_in_json,
)
from .types import EditorCtx

logger = logging.getLogger(__name__)

def handle_zone_confirm(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Handle pending zone confirmation (Y/Enter = accept, N/Esc = cancel)."""
    model = ctx.model
    world = ctx.world
    pending = model.pending_zone_confirm
    key = event.key
    if key in (pygame.K_y, pygame.K_RETURN, pygame.K_KP_ENTER):
        try:
            eid = pending.get('eid')
            orig_zone = pending.get('orig_zone')
            proposed_zone = pending.get('proposed_zone')
            if eid is not None:
                persist_drop(world, eid, getattr(ctx.controller.events, '_drag_start_entry', None), override_zone=proposed_zone, orig_zone=orig_zone)
                try:
                    cfg = world.components['SpawnerConfig'][eid]
                    cfg.zone = proposed_zone
                except (KeyError, AttributeError, TypeError):
                    logger.debug("handle_zone_confirm: failed to update cfg.zone", exc_info=True)
                try:
                    ctx.controller.spawner_instances.refresh_from_disk()
                except AttributeError:
                    logger.debug("handle_zone_confirm: failed to refresh instances from disk", exc_info=True)
        except (OSError, ValueError, TypeError, KeyError, AttributeError):
            logger.debug("handle_zone_confirm: unexpected error on accept", exc_info=True)
        model.pending_zone_confirm = None
        try:
            if hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', False)
        except AttributeError:
            logger.debug("handle_zone_confirm: failed to clear input suppression after accept", exc_info=True)
        try:
            setattr(model, 'tutorial_zone_confirm_yes_pulse', True)
        except AttributeError:
            logger.debug("handle_zone_confirm: failed to set tutorial pulse on accept", exc_info=True)
        return True
    if key in (pygame.K_n, pygame.K_ESCAPE):
        try:
            eid = pending.get('eid')
            orig_zone = pending.get('orig_zone')
            orig_local = pending.get('orig_local')
            if eid is not None and orig_zone and orig_local:
                ox, oy = global_map_settings.zone_offsets.get(orig_zone, (0, 0))
                gx = int(ox + int(orig_local[0]))
                gy = int(oy + int(orig_local[1]))
                cfg = world.components['SpawnerConfig'][eid]
                cfg.anchor_tile = (gx, gy)
        except (KeyError, AttributeError, TypeError, ValueError):
            logger.debug("handle_zone_confirm: failed to restore anchor on cancel", exc_info=True)
        model.pending_zone_confirm = None
        try:
            if hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', False)
        except AttributeError:
            logger.debug("handle_zone_confirm: failed to clear input suppression on cancel", exc_info=True)
        return True
    return False


def handle_delete_confirm(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Handle pending delete confirmation (Y/Enter = accept, N/Esc = cancel)."""
    model = ctx.model
    world = ctx.world
    pending = model.pending_delete_confirm
    key = event.key
    if key in (pygame.K_y, pygame.K_RETURN, pygame.K_KP_ENTER):
        try:
            eid = pending.get('eid')
            tpl_id = pending.get('template_id')
            zone = pending.get('zone')
            local_tile = pending.get('local_tile')
            data, idx, _ = find_instance_in_json(tpl_id, zone, tuple(local_tile))
            # Capture instance id and visuals building ids before removal for runtime cleanup
            inst_id_str = None
            vis_bids: list[int] = []
            try:
                if idx is not None and 0 <= idx < len(data):
                    inst_entry = data[idx]
                    inst_id = inst_entry.get('id')
                    if inst_id is not None:
                        inst_id_str = str(inst_id)
                    vis = inst_entry.get('visuals') if isinstance(inst_entry.get('visuals'), dict) else None
                    if isinstance(vis, dict):
                        for _, v in list(vis.items()):
                            try:
                                if isinstance(v, dict):
                                    bid = v.get('instance_id') or v.get('id') or v.get('building_instance_id')
                                else:
                                    bid = v
                                if bid is not None:
                                    vis_bids.append(int(bid))
                            except Exception:
                                continue
            except Exception:
                inst_id_str = inst_id_str  # keep existing value if any
            try:
                if inst_id_str is not None and hasattr(ctx.controller, 'spawner_instances') and hasattr(ctx.controller.spawner_instances, 'hide_instance_by_id'):
                    ctx.controller.spawner_instances.hide_instance_by_id(inst_id_str)
            except Exception:
                logger.debug("handle_delete_confirm: failed to hide instance id in Instances panel", exc_info=True)
            try:
                if eid is not None:
                    world.remove_entity(eid)
            except AttributeError:
                logger.debug("handle_delete_confirm: failed to remove entity from world", exc_info=True)
            # Remove any pending spawn requests for this spawner
            try:
                reqs = list(getattr(world, 'components', {}).get('SpawnRequest', {}).items())
            except Exception:
                reqs = []
            for req_eid, req in reqs:
                try:
                    if getattr(req, 'spawner_eid', None) == eid:
                        world.remove_entity(req_eid)
                except Exception:
                    pass
            # Remove all NPCs spawned by this spawner (SpawnerChild links)
            try:
                children = list(getattr(world, 'components', {}).get('SpawnerChild', {}).items())
            except Exception:
                children = []
            removed_npcs = 0
            for child_eid, child in children:
                try:
                    if getattr(child, 'spawner_eid', None) == eid:
                        world.remove_entity(child_eid)
                        removed_npcs += 1
                except Exception:
                    pass
            try:
                if removed_npcs:
                    logger.debug("handle_delete_confirm: removed %d spawned NPC(s) for spawner eid=%s", removed_npcs, eid)
            except Exception:
                pass
            # Runtime visuals cleanup: remove any Building objects tied to this spawner instance
            try:
                removed_count = 0
                if getattr(world, 'buildings', None) is not None:
                    new_list = []
                    for ob in list(world.buildings or []):
                        try:
                            ob_id = getattr(ob, 'id', None)
                            sid = getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', None))
                            is_spawner_vis = bool(getattr(ob, '_is_spawner_visual', False))
                            if (ob_id is not None and int(ob_id) in set(vis_bids)) or (inst_id_str is not None and sid is not None and str(sid) == str(inst_id_str)):
                                removed_count += 1
                                continue
                        except Exception:
                            pass
                        new_list.append(ob)
                    world.buildings = new_list
                    # Invalidate spatial index so colliders/indices update
                    try:
                        if hasattr(world, 'invalidate_spatial_index'):
                            world.invalidate_spatial_index()
                    except Exception:
                        pass
                if removed_count:
                    logger.debug("handle_delete_confirm: removed %d runtime visual building(s) for spawner id=%s", removed_count, inst_id_str)
            except Exception:
                logger.debug("handle_delete_confirm: failed to cleanup runtime visuals for deleted spawner", exc_info=True)
            try:
                ctx.controller.spawner_instances.refresh_from_disk()
            except AttributeError:
                logger.debug("handle_delete_confirm: failed to refresh instances from disk", exc_info=True)
            try:
                if hasattr(ctx.controller, 'instance_properties') and getattr(ctx.controller.instance_properties.model, 'visible', False):
                    ctx.controller.instance_properties.model.visible = False
            except AttributeError:
                logger.debug("handle_delete_confirm: failed to hide instance_properties panel", exc_info=True)
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_remove_candidate_eid', None)
                    setattr(world.state, 'spawner_editor_hovered_eid', None)
            except AttributeError:
                logger.debug("handle_delete_confirm: failed to clear world.state flags", exc_info=True)
        except (OSError, ValueError, TypeError, KeyError, AttributeError):
            logger.debug("handle_delete_confirm: unexpected error on accept", exc_info=True)
        model.pending_delete_confirm = None
        try:
            if hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', False)
        except AttributeError:
            logger.debug("handle_delete_confirm: failed to clear input suppression after accept", exc_info=True)
        try:
            setattr(model, 'tutorial_delete_done_pulse', True)
        except AttributeError:
            logger.debug("handle_delete_confirm: failed to set tutorial pulse on accept", exc_info=True)
        return True
    if key in (pygame.K_n, pygame.K_ESCAPE):
        model.pending_delete_confirm = None
        try:
            if hasattr(world, 'state'):
                setattr(world.state, 'spawner_input_suppressed', False)
        except AttributeError:
            logger.debug("handle_delete_confirm: failed to clear input suppression on cancel", exc_info=True)
        return True
    return False
