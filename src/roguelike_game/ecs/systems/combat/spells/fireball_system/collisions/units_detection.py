"""Utilities to detect collisions between fireballs and units.

Optimizado con spatial hash para reducir complejidad de O(n) a O(1) amortizado.
"""
from __future__ import annotations

from typing import Iterable, Optional, Sequence, Tuple, Set

import pygame

from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.utils.spatial_hash import get_combat_spatial_hash, SpatialHash

from ..mask_cache import CircleMaskCache
from ..runtime import FireballRuntime

# Cache del spatial hash actualizado por frame
_last_frame_hash_updated: int = -1


def reset_unit_detection_cache() -> None:
    """Reset the frame cache for unit detection. Call between tests."""
    global _last_frame_hash_updated
    _last_frame_hash_updated = -1


def _update_combat_spatial_hash(world) -> SpatialHash:
    """Actualiza el spatial hash con entidades de combate si es necesario."""
    global _last_frame_hash_updated
    
    spatial = get_combat_spatial_hash()
    
    # Obtener frame actual (usar contador simple si no hay frame_count)
    current_frame = getattr(world, '_frame_count', 0)
    
    # Solo actualizar una vez por frame
    if current_frame == _last_frame_hash_updated:
        return spatial
    
    _last_frame_hash_updated = current_frame
    spatial.clear()
    
    positions = world.components.get("Position", {})
    multi_colliders = world.components.get("MultiCollider", {})
    healths = world.components.get("Health", {})
    death_timers = world.components.get("DeathTimer", {})
    dying_tags = world.components.get("DyingTag", {})
    
    for eid in healths:
        if eid in death_timers or eid in dying_tags:
            continue
        pos = positions.get(eid)
        multi = multi_colliders.get(eid)
        if pos is None or multi is None:
            continue
        
        # Calcular radio aproximado del collider
        radius = 32.0  # Default
        try:
            for collider in multi.colliders.values():
                if hasattr(collider, 'radius'):
                    radius = max(radius, float(collider.radius))
                elif hasattr(collider, 'width'):
                    radius = max(radius, float(collider.width) / 2)
        except Exception:
            pass
        
        spatial.insert(eid, pos.x, pos.y, radius)
    
    return spatial


def find_unit_collision(
    runtime: FireballRuntime,
    sample_points: Sequence[Tuple[float, float]],
    mask_cache: CircleMaskCache,
) -> Optional[Tuple[int, Tuple[float, float], str]]:
    """Return the first collider hit by the projectile if any.
    
    Optimizado: usa spatial hash para broad-phase culling.
    """
    world = runtime.world
    hit_radius = runtime.hit_radius
    
    # Obtener candidatos del spatial hash (broad-phase)
    spatial = _update_combat_spatial_hash(world)
    
    # Calcular centro y radio de búsqueda desde sample_points
    if sample_points:
        min_x = min(p[0] for p in sample_points)
        max_x = max(p[0] for p in sample_points)
        min_y = min(p[1] for p in sample_points)
        max_y = max(p[1] for p in sample_points)
        center_x = (min_x + max_x) / 2
        center_y = (min_y + max_y) / 2
        search_radius = max(max_x - min_x, max_y - min_y) / 2 + hit_radius + 64
    else:
        center_x = runtime.position.x
        center_y = runtime.position.y
        search_radius = hit_radius + 64
    
    # Obtener candidatos cercanos
    candidates: Set[int] = spatial.query_radius(center_x, center_y, search_radius)
    
    # Excluir self y caster
    candidates.discard(runtime.entity_id)
    if runtime.component.caster is not None:
        candidates.discard(runtime.component.caster)
    
    # Narrow-phase: verificar colisión precisa
    for target in candidates:
        multi = world.components.get("MultiCollider", {}).get(target)
        position = world.components.get("Position", {}).get(target)
        if multi is None or position is None:
            continue
        
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
