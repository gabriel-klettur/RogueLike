"""Collision helpers for projectile interactions with solid tiles."""
from __future__ import annotations

from typing import Optional, Sequence, Tuple

import pygame

from ..effects import spawn_impact_effects
from ..runtime import FireballRuntime, get_scale_multiplier


def handle_tile_collisions(
    runtime: FireballRuntime,
    sample_points: Sequence[Tuple[float, float]],
    path_aabb: Optional[pygame.Rect],
) -> bool:
    """Return ``True`` if the projectile collides with a solid tile."""

    if path_aabb is None:
        return False

    try:
        nearby_tiles = runtime.world.get_solid_tiles_for_rect(path_aabb)
    except Exception:
        nearby_tiles = None

    if not nearby_tiles:
        return False

    hit_point: Optional[Tuple[float, float]] = None
    radius = runtime.hit_radius

    for sx, sy in sample_points:
        circle_rect = pygame.Rect(
            int(sx - radius),
            int(sy - radius),
            int(2 * radius) + 1,
            int(2 * radius) + 1,
        )
        if any(tile.colliderect(circle_rect) for tile in nearby_tiles):
            hit_point = (float(sx), float(sy))
            break

    if hit_point is None:
        return False

    spawn_impact_effects(
        runtime.world,
        runtime.config,
        hit_point,
        get_scale_multiplier(runtime.component),
    )
    runtime.world.remove_entity(runtime.entity_id)
    return True
