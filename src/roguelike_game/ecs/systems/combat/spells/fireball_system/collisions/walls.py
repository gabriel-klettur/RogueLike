"""Collision helpers for projectile-to-wall interactions."""
from __future__ import annotations

from typing import Iterable, Optional, Sequence, Tuple

import pygame

from ..effects import spawn_impact_effects
from ..mask_cache import CircleMaskCache
from ..runtime import FireballRuntime, get_scale_multiplier


class WallCacheEntry:
    """Data snapshot for a wall segment used during collision checks."""

    __slots__ = ("aabb", "wx", "wy", "half_w", "half_h", "cos", "sin")

    def __init__(
        self,
        aabb: pygame.Rect,
        wx: float,
        wy: float,
        half_w: float,
        half_h: float,
        cos: float,
        sin: float,
    ) -> None:
        self.aabb = aabb
        self.wx = wx
        self.wy = wy
        self.half_w = half_w
        self.half_h = half_h
        self.cos = cos
        self.sin = sin


def precompute_wall_cache(world: object) -> list[WallCacheEntry]:
    """Return cached wall data for the current frame."""

    entries: list[WallCacheEntry] = []
    walls = world.components.get("WallSegmentComponent", {})
    positions = world.components.get("Position", {})
    for wall_id, wall in list(walls.items()):
        try:
            if not bool(getattr(wall, "blocks_projectiles", True)):
                continue
            pos = positions.get(wall_id)
            if pos is None:
                continue
            half_w = float(getattr(wall, "half_w", getattr(wall, "width", 0.0) * 0.5) or 0.0)
            half_h = float(getattr(wall, "half_h", getattr(wall, "height", 0.0) * 0.5) or 0.0)
            cos_a = float(getattr(wall, "cos_a", 1.0))
            sin_a = float(getattr(wall, "sin_a", 0.0))
            ext_x = abs(cos_a) * half_w + abs(sin_a) * half_h
            ext_y = abs(sin_a) * half_w + abs(cos_a) * half_h
            aabb = pygame.Rect(
                int(pos.x - ext_x),
                int(pos.y - ext_y),
                int(ext_x * 2),
                int(ext_y * 2),
            )
            entries.append(
                WallCacheEntry(
                    aabb=aabb,
                    wx=float(pos.x),
                    wy=float(pos.y),
                    half_w=half_w,
                    half_h=half_h,
                    cos=cos_a,
                    sin=sin_a,
                )
            )
        except Exception:
            continue
    return entries


def handle_wall_collision(
    runtime: FireballRuntime,
    sample_points: Sequence[Tuple[float, float]],
    path_aabb: Optional[pygame.Rect],
    walls_cache: Iterable[WallCacheEntry],
    mask_cache: CircleMaskCache,
) -> bool:
    """Resolve projectile collisions against blocking wall segments."""

    if path_aabb is None:
        return False

    circle_mask, radius = mask_cache.get(runtime.hit_radius)
    hit_point: Optional[Tuple[float, float]] = None

    for sx, sy in sample_points:
        circle_rect = pygame.Rect(int(sx - radius), int(sy - radius), 2 * radius + 1, 2 * radius + 1)
        for wall in walls_cache:
            if not circle_rect.colliderect(wall.aabb):
                continue
            if _circle_overlaps_wall(sx, sy, radius, wall):
                hit_point = (float(sx), float(sy))
                break
        if hit_point is not None:
            break

    if hit_point is None:
        return False

    scale = get_scale_multiplier(runtime.component)
    spawn_impact_effects(runtime.world, runtime.config, hit_point, scale)
    runtime.world.remove_entity(runtime.entity_id)
    return True


def _circle_overlaps_wall(
    sx: float,
    sy: float,
    radius: int,
    wall: WallCacheEntry,
) -> bool:
    from roguelike_game.ecs.utils.collider_utils import circle_overlaps_obb

    return circle_overlaps_obb(
        sx,
        sy,
        radius,
        wall.wx,
        wall.wy,
        wall.half_w,
        wall.half_h,
        wall.cos,
        wall.sin,
    )
