"""Runtime helpers for the FireballSystem."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

import math
import importlib
import pygame

from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity


@dataclass
class FireballRuntime:
    """Aggregates frequently accessed data for a single fireball update."""

    world: Any
    entity_id: int
    component: FireballComponent
    position: Position
    velocity: Velocity
    prev_x: float
    prev_y: float
    hit_radius: float
    config: Dict[str, Any]
    sample_points: List[Tuple[float, float]] = field(default_factory=list)
    path_aabb: Optional[pygame.Rect] = None

    @property
    def speed(self) -> float:
        """Return projectile speed magnitude based on current velocity."""
        return math.hypot(self.velocity.vx, self.velocity.vy)

    @property
    def displacement(self) -> Tuple[float, float]:
        """Vector travelled during the last tick."""
        return self.position.x - self.prev_x, self.position.y - self.prev_y


def build_runtime(world: Any, entity_id: int) -> Optional[FireballRuntime]:
    """Assemble a :class:`FireballRuntime` from ECS component storage.

    Args:
        world: ECS world exposing ``components`` dict and ``remove_entity``.
        entity_id: Identifier of the projectile entity.

    Returns:
        A populated :class:`FireballRuntime` or ``None`` if mandatory components
        are missing.
    """
    try:
        component = world.components['FireballComponent'][entity_id]
        position = world.components['Position'][entity_id]
        velocity = world.components['Velocity'][entity_id]
    except Exception:
        return None

    config = _resolve_spells().get(getattr(component, 'spell_key', ''), {})
    try:
        hit_radius = float(getattr(component, 'hit_radius', 2.0))
    except Exception:
        hit_radius = 2.0

    return FireballRuntime(
        world=world,
        entity_id=entity_id,
        component=component,
        position=position,
        velocity=velocity,
        prev_x=float(position.x),
        prev_y=float(position.y),
        hit_radius=hit_radius,
        config=config,
    )


def advance(runtime: FireballRuntime) -> bool:
    """Advance projectile position and age.

    Returns ``False`` if the entity was removed due to lifespan expiry.
    """
    position = runtime.position
    velocity = runtime.velocity
    component = runtime.component
    world = runtime.world

    runtime.prev_x = float(position.x)
    runtime.prev_y = float(position.y)

    position.x += velocity.vx
    position.y += velocity.vy
    component.age += 1

    lifespan = getattr(component, 'lifespan', 0)
    try:
        lifespan_int = int(lifespan)
    except Exception:
        lifespan_int = 0
    if lifespan_int and component.age >= lifespan_int:
        world.remove_entity(runtime.entity_id)
        return False
    return True


def exceeds_range(runtime: FireballRuntime) -> bool:
    """Return ``True`` and remove the entity if it exceeded configured range."""
    max_range = runtime.config.get('range', 0)
    spawn_pos = getattr(runtime.component, 'spawn_pos', None)
    if not (max_range and spawn_pos):
        return False

    dx = runtime.position.x - float(spawn_pos[0])
    dy = runtime.position.y - float(spawn_pos[1])
    if math.hypot(dx, dy) > float(max_range):
        runtime.world.remove_entity(runtime.entity_id)
        return True
    return False


def compute_sampling(runtime: FireballRuntime, max_samples: int = 12) -> None:
    """Populate ``sample_points`` and ``path_aabb`` for the runtime."""

    dx, dy = runtime.displacement
    distance = math.hypot(dx, dy)
    step = max(1.0, float(runtime.hit_radius) * 0.5)
    samples = max(1, int(distance / step))
    samples = min(samples, max_samples)

    if samples <= 1:
        runtime.sample_points = [(runtime.position.x, runtime.position.y)]
    else:
        points: List[Tuple[float, float]] = []
        prev_x, prev_y = runtime.prev_x, runtime.prev_y
        for i in range(samples + 1):
            t = i / samples
            sx = prev_x + dx * t
            sy = prev_y + dy * t
            points.append((sx, sy))
        runtime.sample_points = points

    left = int(min(runtime.prev_x, runtime.position.x) - runtime.hit_radius)
    top = int(min(runtime.prev_y, runtime.position.y) - runtime.hit_radius)
    right = int(max(runtime.prev_x, runtime.position.x) + runtime.hit_radius)
    bottom = int(max(runtime.prev_y, runtime.position.y) + runtime.hit_radius)
    runtime.path_aabb = pygame.Rect(
        left,
        top,
        max(1, right - left + 1),
        max(1, bottom - top + 1),
    )


def get_scale_multiplier(component: FireballComponent) -> float:
    """Return the VFX scale multiplier stored on the component."""

    try:
        return float(getattr(component, "vfx_scale_multiplier", 1.0))
    except Exception:
        return 1.0


def _resolve_spells() -> Dict[str, Any]:
    """Resolve the SPELLS mapping.

    Prefer the package-level attribute so tests can monkeypatch
    ``fireball_system.SPELLS`` directly. Fall back to global config if absent.
    """
    try:
        pkg = importlib.import_module('roguelike_game.ecs.systems.combat.spells.fireball_system')
        spells = getattr(pkg, 'SPELLS', None)
        if isinstance(spells, dict):
            return spells
    except Exception:
        pass
    try:
        from roguelike_game.config.spells_config import SPELLS as CFG
        return CFG
    except Exception:
        return {}
