from __future__ import annotations

import pygame
from typing import Optional
import logging

from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services import (
    persist_drop,
    find_instance_in_json,
    write_instances_json,
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
            if idx is not None:
                try:
                    data.pop(idx)
                except (IndexError, AttributeError):
                    logger.debug("handle_delete_confirm: failed to pop instance from list", exc_info=True)
                try:
                    write_instances_json(data)
                except OSError:
                    logger.debug("handle_delete_confirm: failed to write instances after delete", exc_info=True)
            try:
                if eid is not None:
                    world.remove_entity(eid)
            except AttributeError:
                logger.debug("handle_delete_confirm: failed to remove entity from world", exc_info=True)
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
