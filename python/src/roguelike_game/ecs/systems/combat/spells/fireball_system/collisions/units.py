"""Collision helpers for fireball impacts against units (NPCs and player)."""
from __future__ import annotations

import time
from typing import Optional, Tuple

import pygame

from roguelike_game.ecs.components.combat.last_attacker import LastAttacker
from roguelike_game.ecs.components.rendering.flash_component import FlashComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.utils.health_utils import is_neutral

from ..effects import spawn_impact_effects
from ..mask_cache import CircleMaskCache
from ..runtime import FireballRuntime, get_scale_multiplier
from .units_detection import find_unit_collision


class CollisionResult:
    """Data returned when a collision with an entity is detected."""

    __slots__ = ("entity_id", "position", "shape")

    def __init__(self, entity_id: int, position: Tuple[float, float], shape: str) -> None:
        self.entity_id = entity_id
        self.position = position
        self.shape = shape


def handle_unit_collisions(
    runtime: FireballRuntime,
    sample_points,
    mask_cache: CircleMaskCache,
) -> Optional[CollisionResult]:
    """Return the first collision with a non-neutral unit if any."""

    world = runtime.world

    result = find_unit_collision(runtime, sample_points, mask_cache)
    if result is None:
        return None

    target, position, shape = result

    if is_neutral(world, target):
        world.remove_entity(runtime.entity_id)
        return None

    return CollisionResult(target, position, shape)


def apply_combat_effects(runtime: FireballRuntime, collision: CollisionResult) -> None:
    """Apply damage, status updates, and feedback after a collision."""

    world = runtime.world
    target = collision.entity_id
    is_player = target in world.components.get("PlayerTagComponent", {})
    godmode_state = getattr(getattr(world, "state", None), "godmode", False)
    godmode_target = bool(godmode_state) and is_player
    caster_is_player = runtime.component.caster in world.components.get("PlayerTagComponent", {})

    if not godmode_target:
        health = world.components["Health"][target]
        if caster_is_player and bool(godmode_state):
            health.current_hp = 0
        else:
            health.current_hp = max(0, health.current_hp - runtime.component.damage)
        # White hit flash for any normal damage (status flashes take precedence in FlashSystem)
        try:
            flashes = world.components.setdefault("FlashComponent", {})
            flashes[target] = FlashComponent((255, 255, 255), 0.12)
        except Exception:
            pass
        world.components.setdefault("LastAttacker", {})[target] = LastAttacker(runtime.component.caster, time.time())

    spawn_impact_effects(
        world,
        runtime.config,
        collision.position,
        get_scale_multiplier(runtime.component),
    )

    _push_debug_event(world, runtime.entity_id, target, collision)
    world.remove_entity(runtime.entity_id)
    _push_fsm_events(runtime, target, godmode_target)


def _push_debug_event(world: object, src: int, target: int, collision: CollisionResult) -> None:
    queue = world.components.setdefault("DebugSpellHits", {}).setdefault("_queue", [])
    queue.append(
        {
            "type": "FB",
            "src": src,
            "target": target,
            "pos": collision.position,
            "shape": collision.shape,
        }
    )


def _push_fsm_events(runtime: FireballRuntime, target: int, godmode_target: bool) -> None:
    world = runtime.world
    caster = runtime.component.caster

    if caster in world.components.get("PlayerTagComponent", {}):
        attacker_pos = world.components["Position"].get(caster)
        defender_pos = world.components["Position"].get(target)
        if attacker_pos and defender_pos:
            from_left = attacker_pos.x < defender_pos.x
        else:
            from_left = False
        queue = world.components.setdefault("FSMEventQueue", {}).setdefault(target, [])
        queue.append({"type": "OnHit", "from_left": from_left})
        if not godmode_target:
            combo_queue = world.components.setdefault("ComboEventQueue", [])
            combo_queue.append(
                {
                    "attacker": caster,
                    "target": target,
                    "damage": float(runtime.component.damage),
                    "source": "fireball",
                    "time": float(time.time()),
                }
            )
        hud = world.components.setdefault("TargetHUD", {})
        hud["target_eid"] = int(target)
        hud["last_hit_time"] = float(time.time())
        hud.setdefault("ttl_s", 3.0)
    elif target in world.components.get("PlayerTagComponent", {}) and not godmode_target:
        attacker_pos = world.components["Position"].get(caster)
        defender_pos = world.components["Position"].get(target)
        if attacker_pos and defender_pos:
            from_left = attacker_pos.x < defender_pos.x
        else:
            from_left = False
        queue = world.components.setdefault("FSMEventQueue", {}).setdefault(target, [])
        queue.append({"type": "OnHit", "from_left": from_left})
        combo_queue = world.components.setdefault("ComboEventQueue", [])
        combo_queue.append({"type": "break", "entity": target})
