"""Collision helpers for fireball interactions with building visuals."""
from __future__ import annotations

from typing import Optional, Sequence, Tuple

import pygame

from ..effects import spawn_impact_effects
from ..mask_cache import CircleMaskCache
from ..runtime import FireballRuntime, get_scale_multiplier

_FIREBALL_EVENT_SOURCE = "fireball"


def handle_building_collision(
    runtime: FireballRuntime,
    sample_points: Sequence[Tuple[float, float]],
    path_aabb: Optional[pygame.Rect],
    mask_cache: CircleMaskCache,
) -> bool:
    """Resolve collisions against building visuals.

    Returns ``True`` if the projectile was removed as a result of the collision.
    """
    world = runtime.world
    buildings: Iterable[object] = getattr(world, "buildings", []) or []
    if not buildings:
        return False

    hit_spawner_eid: Optional[int] = None
    impact_pos: Optional[Tuple[float, float]] = None

    for building in buildings:
        try:
            if getattr(building, "runtime_hidden", False):
                continue
            if not bool(getattr(building, "_is_spawner_visual", False)):
                continue
            if not _is_damageable(building):
                continue

            if not _path_intersects_building(path_aabb, building):
                continue

            model = getattr(building, "model", None)
            mask_exists = False
            if model is not None and hasattr(model, "get_full_mask"):
                try:
                    mask_exists = model.get_full_mask() is not None
                except Exception:
                    mask_exists = False

            impact_pos = _mask_collision(runtime, building, sample_points, mask_cache)
            if impact_pos is not None:
                hit_spawner_eid = _get_spawner_eid(building)
                break
            if not mask_exists:
                impact_pos = _rect_collision(runtime, building, sample_points)
                if impact_pos is not None:
                    hit_spawner_eid = _get_spawner_eid(building)
                    break
        except Exception:
            # Keep behaviour tolerant to malformed building instances.
            continue

    if hit_spawner_eid is None:
        return False

    world.components.setdefault("SpawnerDamageEvents", []).append(
        {
            "spawner_eid": hit_spawner_eid,
            "damage": float(runtime.component.damage),
            "attacker": int(runtime.component.caster) if runtime.component.caster is not None else None,
        }
    )

    scale_mul = get_scale_multiplier(runtime.component)
    position = impact_pos or (runtime.position.x, runtime.position.y)
    spawn_impact_effects(world, runtime.config, position, scale_mul)

    world.remove_entity(runtime.entity_id)
    return True


def _is_damageable(building: object) -> bool:
    life_cfg = getattr(building, "_spawner_visual_life_cfg", None) or {}
    if isinstance(life_cfg, dict):
        return bool(life_cfg.get("damageable", False))
    return False


def _path_intersects_building(path_aabb: Optional[pygame.Rect], building: object) -> bool:
    if path_aabb is None:
        return True
    model = getattr(building, "model", None)
    if model and getattr(model, "image", None) is not None:
        width, height = model.image.get_size()
        building_rect = pygame.Rect(int(building.x), int(building.y), int(width), int(height))
        return path_aabb.colliderect(building_rect)
    rect = getattr(building, "rect", None) or getattr(building, "collision_rect", None)
    if rect:
        return path_aabb.colliderect(rect)
    return True


def _mask_collision(
    runtime: FireballRuntime,
    building: object,
    sample_points: Sequence[Tuple[float, float]],
    mask_cache: CircleMaskCache,
) -> Optional[Tuple[float, float]]:
    model = getattr(building, "model", None)
    if model is None:
        return None
    mask = model.get_full_mask() if hasattr(model, "get_full_mask") else None
    if mask is None:
        return None

    circle_mask, radius_int = mask_cache.get(runtime.hit_radius)

    for sx, sy in sample_points:
        local_x = int(round(sx - building.x))
        local_y = int(round(sy - building.y))
        offset = (local_x - radius_int, local_y - radius_int)
        if mask.overlap(circle_mask, offset) is not None:
            return (float(sx), float(sy))
    return None


def _rect_collision(
    runtime: FireballRuntime,
    building: object,
    sample_points: Sequence[Tuple[float, float]],
) -> Optional[Tuple[float, float]]:
    rect = getattr(building, "rect", None) or getattr(building, "collision_rect", None)
    if rect is None:
        return None

    tiles = list(getattr(building, "collision_tiles", []) or [])
    hit_radius = runtime.hit_radius

    for sx, sy in sample_points:
        circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2 * hit_radius) + 1, int(2 * hit_radius) + 1)
        if rect.colliderect(circle_rect):
            if not tiles:
                return (float(sx), float(sy))
            if any(tile.colliderect(circle_rect) for tile in tiles):
                return (float(sx), float(sy))
    return None


def _get_spawner_eid(building: object) -> Optional[int]:
    se = getattr(building, "_spawner_eid", None)
    return int(se) if se is not None else None
