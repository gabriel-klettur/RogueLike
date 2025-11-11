"""Utilities to detect collisions between fireballs and units."""
from __future__ import annotations

from typing import Iterable, Optional, Sequence, Tuple

import pygame

from roguelike_game.ecs.components.transform.position import Position

from ..mask_cache import CircleMaskCache
from ..runtime import FireballRuntime


def find_unit_collision(
    runtime: FireballRuntime,
    sample_points: Sequence[Tuple[float, float]],
    mask_cache: CircleMaskCache,
) -> Optional[Tuple[int, Tuple[float, float], str]]:
    """Return the first collider hit by the projectile if any."""

    world = runtime.world
    hit_radius = runtime.hit_radius

    for target in world.get_entities_with("Position", "MultiCollider", "Health"):
        if target == runtime.entity_id or target == runtime.component.caster:
            continue
        if target in world.components.get("DeathTimer", {}) or target in world.components.get("DyingTag", {}):
            continue

        multi = world.components["MultiCollider"][target]
        position = world.components["Position"][target]
        if not _path_overlaps_entity(runtime.path_aabb, position, multi, hit_radius):
            continue

        impact = _mask_first_hit(position, multi.colliders.values(), sample_points, mask_cache, hit_radius)
        if impact is None:
            impact = _circle_first_hit(position, multi.colliders.values(), sample_points, hit_radius)
        if impact is None:
            impact = _rect_first_hit(position, multi.colliders.values(), sample_points, hit_radius)
        if impact is None:
            continue

        return target, impact[0], impact[1]
    return None


def _path_overlaps_entity(
    path_aabb: Optional[pygame.Rect],
    position: Position,
    multi_collider,
    hit_radius: float,
) -> bool:
    if path_aabb is None:
        return True

    rects = []
    try:
        for collider in multi_collider:
            rect = _collider_rect(position, collider)
            if rect is not None:
                rects.append(rect)
    except Exception:
        rects = []

    if not rects:
        return True

    union = rects[0].copy()
    for rect in rects[1:]:
        union.union_ip(rect)
    union.inflate_ip(int(2 * hit_radius) + 1, int(2 * hit_radius) + 1)
    return path_aabb.colliderect(union)


def _collider_rect(position: Position, collider: object) -> Optional[pygame.Rect]:
    if hasattr(collider, "radius"):
        cx = int(position.x + getattr(collider, "offset_x", 0))
        cy = int(position.y + getattr(collider, "offset_y", 0))
        radius = int(getattr(collider, "radius", 0))
        return pygame.Rect(cx - radius, cy - radius, radius * 2 + 1, radius * 2 + 1)
    if hasattr(collider, "mask"):
        ax = int(position.x + getattr(collider, "offset_x", 0))
        ay = int(position.y + getattr(collider, "offset_y", 0))
        try:
            width, height = collider.mask.get_size()
        except Exception:
            width, height = 0, 0
        return pygame.Rect(ax, ay, int(width), int(height))
    if hasattr(collider, "width") and hasattr(collider, "height"):
        ax = int(position.x + getattr(collider, "offset_x", 0))
        ay = int(position.y + getattr(collider, "offset_y", 0))
        return pygame.Rect(ax, ay, int(getattr(collider, "width", 0)), int(getattr(collider, "height", 0)))
    return None


def _mask_first_hit(
    position: Position,
    colliders: Iterable[object],
    sample_points: Sequence[Tuple[float, float]],
    mask_cache: CircleMaskCache,
    hit_radius: float,
) -> Optional[Tuple[Tuple[float, float], str]]:
    for collider in colliders:
        if not hasattr(collider, "mask"):
            continue
        circle_mask, radius = mask_cache.get(hit_radius)
        for sx, sy in sample_points:
            local_x = int(round(sx - (position.x + collider.offset_x)))
            local_y = int(round(sy - (position.y + collider.offset_y)))
            offset = (local_x - radius, local_y - radius)
            if collider.mask.overlap(circle_mask, offset) is not None:
                return (float(sx), float(sy)), "mask"
    return None


def _circle_first_hit(
    position: Position,
    colliders: Iterable[object],
    sample_points: Sequence[Tuple[float, float]],
    hit_radius: float,
) -> Optional[Tuple[Tuple[float, float], str]]:
    for collider in colliders:
        if not hasattr(collider, "radius"):
            continue
        cx = float(position.x + getattr(collider, "offset_x", 0))
        cy = float(position.y + getattr(collider, "offset_y", 0))
        cr = float(getattr(collider, "radius", 0))
        if cr <= 0:
            continue
        for sx, sy in sample_points:
            dx = sx - cx
            dy = sy - cy
            if (dx * dx + dy * dy) <= (hit_radius + cr) ** 2:
                return (float(sx), float(sy)), "circle"
    return None


def _rect_first_hit(
    position: Position,
    colliders: Iterable[object],
    sample_points: Sequence[Tuple[float, float]],
    hit_radius: float,
) -> Optional[Tuple[Tuple[float, float], str]]:
    for collider in colliders:
        if hasattr(collider, "mask"):
            continue
        rect = pygame.Rect(
            position.x + getattr(collider, "offset_x", 0),
            position.y + getattr(collider, "offset_y", 0),
            getattr(collider, "width", 0),
            getattr(collider, "height", 0),
        )
        for sx, sy in sample_points:
            circle_rect = pygame.Rect(
                int(sx - hit_radius),
                int(sy - hit_radius),
                int(2 * hit_radius) + 1,
                int(2 * hit_radius) + 1,
            )
            if rect.colliderect(circle_rect):
                return (float(sx), float(sy)), "rect"
    return None
