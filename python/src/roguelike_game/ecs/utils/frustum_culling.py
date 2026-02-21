"""
Frustum culling utilities for ECS update systems.

Provides helpers to determine if an entity is within the "active zone"
around the camera. Entities outside this zone can skip expensive per-frame
updates (FSM, animation, separation) to save CPU when many NPCs exist.

Active zone = camera viewport expanded by a configurable margin.
"""
from __future__ import annotations

from typing import Optional, Set
import pygame


# Default margin around the viewport (in world pixels) for the active zone.
# Entities within viewport + margin are considered "active".
_DEFAULT_MARGIN_PX: float = 512.0


def get_active_world_rect(
    camera,
    screen_w: int,
    screen_h: int,
    margin_px: float = _DEFAULT_MARGIN_PX,
) -> Optional[pygame.Rect]:
    """Return the expanded world-space rect around the camera viewport.

    Returns None if camera is missing required attributes.
    """
    try:
        zoom = getattr(camera, 'zoom', 1.0) or 1.0
        ox = getattr(camera, 'offset_x', 0.0)
        oy = getattr(camera, 'offset_y', 0.0)
    except Exception:
        return None

    world_w = screen_w / zoom
    world_h = screen_h / zoom
    return pygame.Rect(
        int(ox - margin_px),
        int(oy - margin_px),
        int(world_w + 2 * margin_px),
        int(world_h + 2 * margin_px),
    )


def get_active_entity_ids(
    world,
    camera,
    margin_px: float = _DEFAULT_MARGIN_PX,
) -> Optional[Set[int]]:
    """Return set of entity IDs whose Position falls within the active zone.

    Returns None if camera info is unavailable (caller should fall back to
    processing all entities).
    """
    if camera is None:
        return None

    try:
        screen = world.screen
        sw, sh = screen.get_size()
    except Exception:
        return None

    active_rect = get_active_world_rect(camera, sw, sh, margin_px)
    if active_rect is None:
        return None

    pos_map = world.components.get('Position', {})
    active: Set[int] = set()
    for eid, pos in pos_map.items():
        if active_rect.collidepoint(pos.x, pos.y):
            active.add(eid)
    return active
