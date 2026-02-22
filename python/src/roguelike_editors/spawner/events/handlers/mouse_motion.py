from __future__ import annotations

import logging
import pygame

from .. import split_drag as split
from .. import resize as rz
from .. import anchor_drag as anchor
from ...services.picking import pick_spawner_under_cursor
from roguelike_engine.config.config_tiles import TILE_SIZE


def handle_mousemotion(h, ctx, event: pygame.event.Event) -> bool:
    """Handle all MOUSEMOTION branches: split drag, hover, resize, anchor drag, moving visual."""
    logger = logging.getLogger(__name__)
    model = h.model
    world, camera = ctx.world, ctx.camera

    # Split drag MOTION while active
    if event.type == pygame.MOUSEMOTION and getattr(model, 'split_drag_active', False) and getattr(model, 'split_drag_bid', None) is not None:
        try:
            if split.update_split_drag(ctx, event):
                return True
        except Exception:
            logger.debug("mouse_motion: split.update_split_drag failed", exc_info=True)

    # Hover: detect spawner anchor under cursor when not dragging/resizing/splitting
    if event.type == pygame.MOUSEMOTION and not getattr(model, 'dragging', False) and not getattr(model, 'resizing_visual', False) and not getattr(model, 'split_drag_active', False):
        try:
            mx, my = event.pos
            eid = pick_spawner_under_cursor(world, camera, int(mx), int(my))
            try:
                model.hovered_eid = eid
            except Exception:
                pass
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_hovered_eid', eid)
            except Exception:
                pass
        except Exception:
            logger.debug("mouse_motion: hover pick failed", exc_info=True)

    # Resize MOTION while active
    if event.type == pygame.MOUSEMOTION and getattr(model, 'resizing_visual', False):
        try:
            if rz.update_resize_motion(ctx, event):
                return True
        except Exception:
            logger.debug("mouse_motion: rz.update_resize_motion failed", exc_info=True)

    # Spawner anchor drag MOTION
    if event.type == pygame.MOUSEMOTION and getattr(model, 'dragging', False) and getattr(model, 'dragging_eid', None) is not None:
        try:
            if anchor.update_anchor_drag_motion(ctx, event):
                return True
        except Exception:
            logger.debug("mouse_motion: anchor.update_anchor_drag_motion failed", exc_info=True)

    # Visual building move MOTION (RMB drag)
    if event.type == pygame.MOUSEMOTION and getattr(model, 'moving_visual', False) and getattr(model, 'moving_visual_bid', None) is not None:
        try:
            ip = getattr(h.controller, 'instance_properties', None)
            ob = None
            bid = int(model.moving_visual_bid)
            if ip is not None and hasattr(ip, 'visuals'):
                try:
                    ob = ip.visuals._find_building_entity_by_id(bid)
                except Exception:
                    ob = None
            if ob is not None:
                # Compute new world top-left from mouse + delta
                mx, my = event.pos
                z = getattr(camera, 'zoom', 1.0) or 1.0
                wx = int(mx / z + camera.offset_x)
                wy = int(my / z + camera.offset_y)
                dx, dy = h._moving_visual_delta_world or (0, 0)
                world_x = int(wx + dx)
                world_y = int(wy + dy)
                # Convert to zone-relative px for rel_x/rel_y
                zone = getattr(ob, 'zone', None)
                if zone is None:
                    try:
                        zone = getattr(getattr(ob, 'model', ob), 'zone', None)
                    except Exception:
                        zone = None
                if not zone:
                    zone = 'lobby'
                try:
                    from roguelike_engine.config.map_config import global_map_settings as _gms
                    off_x, off_y = _gms.zone_offsets.get(str(zone), (0, 0))
                except Exception:
                    off_x, off_y = (0, 0)
                rel_x = int(world_x - int(off_x) * TILE_SIZE)
                rel_y = int(world_y - int(off_y) * TILE_SIZE)
                try:
                    setattr(ob, 'rel_x', rel_x)
                    setattr(ob, 'rel_y', rel_y)
                except Exception:
                    pass
            return True
        except Exception:
            logger = logging.getLogger(__name__)
            logger.debug("mouse_motion: moving visual motion failed", exc_info=True)
            return True

    return False
