"""Geometry helpers for projectile spell spawning."""

from __future__ import annotations

from typing import Tuple

import pygame

from roguelike_game.ecs.utils.position_utils import compute_entity_center

from ...spell_release_context import SpellReleaseContext
from ...release_utils import coerce_float, normalise_vector


def compute_spawn_position(context: SpellReleaseContext) -> Tuple[float, float]:
    """Return the world coordinates used to spawn the projectile."""

    if context.spell_type != "projectile":
        return context.get_spawn_position()

    world = context.world
    if world is None:
        return context.get_spawn_position()

    components = getattr(world, "components", {})
    pos_map = components.get("Position", {})
    sprite_map = components.get("Sprite", {})
    scale_map = components.get("Scale", {})

    caster_pos = pos_map.get(context.entity.id)
    if caster_pos is None:
        return context.get_spawn_position()

    caster_sprite = sprite_map.get(context.entity.id)
    if caster_sprite is None:
        return float(getattr(caster_pos, "x", 0.0)), float(getattr(caster_pos, "y", 0.0))

    caster_scale = scale_map.get(context.entity.id)
    try:
        centre = compute_entity_center(caster_pos, caster_sprite, caster_scale)
        return float(centre.x), float(centre.y)
    except Exception:
        width, height = caster_sprite.image.get_size()
        return (
            float(getattr(caster_pos, "x", 0.0)) + width / 2.0,
            float(getattr(caster_pos, "y", 0.0)) + height / 2.0,
        )


def compute_direction(context: SpellReleaseContext, spawn: Tuple[float, float]) -> Tuple[float, float]:
    """Determine the initial direction for the projectile."""

    lock_direction = bool(context.cfg_value("lock_cast_direction", True))
    if bool(context.context.get("force_lock_direction", False)):
        lock_direction = True

    raw_direction = context.context.get("direction")
    has_context_direction = isinstance(raw_direction, (tuple, list)) and len(raw_direction) >= 2

    # If lock_cast_direction is False, always re-aim using current mouse position
    # for each release, regardless of any previously stored context direction.
    if not lock_direction:
        try:
            mouse_x, mouse_y = pygame.mouse.get_pos()
            camera = context.camera
            if camera:
                world_x = mouse_x / coerce_float(getattr(camera, "zoom", 1.0), default=1.0) + coerce_float(
                    getattr(camera, "offset_x", 0.0),
                    default=0.0,
                )
                world_y = mouse_y / coerce_float(getattr(camera, "zoom", 1.0), default=1.0) + coerce_float(
                    getattr(camera, "offset_y", 0.0),
                    default=0.0,
                )
            else:
                world_x, world_y = mouse_x, mouse_y
            direction = (world_x - spawn[0], world_y - spawn[1])
        except Exception:
            # Fallback to any provided direction or a default
            direction = raw_direction if has_context_direction else (1.0, 0.0)
        return normalise_vector(direction)

    # Locked direction: respect provided context direction; otherwise use a safe default
    direction = raw_direction if has_context_direction else (1.0, 0.0)
    return normalise_vector(direction)
