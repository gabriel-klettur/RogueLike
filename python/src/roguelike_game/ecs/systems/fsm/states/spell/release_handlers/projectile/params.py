"""Parameter extraction for projectile spell handling."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Optional

from ...spell_release_context import SpellReleaseContext
from ...release_utils import coerce_float, load_image_safe


@dataclass(frozen=True)
class ProjectileParams:
    """Runtime values needed to spawn a projectile entity."""

    speed: float
    scale_multiplier: float
    effective_scale: float
    hit_radius: float
    damage: float
    lifespan: float
    sprite_path: Optional[str]
    sprite_surface: Any | None


def build_projectile_params(context: SpellReleaseContext) -> ProjectileParams:
    """Collect core projectile parameters from context and configuration."""

    speed = _resolve_speed(context)
    scale_multiplier = coerce_float(
        context.context.get(
            "scale_multiplier",
            context.cfg_value("scale_multiplier", 1.0),
        ),
        default=1.0,
    )
    effective_scale = coerce_float(context.cfg_value("scale", 1.0), default=1.0) * max(scale_multiplier, 1e-6)

    base_hit_radius = coerce_float(
        context.context.get("hit_radius", context.cfg_value("hit_radius", 2.0)),
        default=2.0,
    )
    hit_radius_multiplier = coerce_float(
        context.context.get(
            "hit_radius_multiplier",
            context.cfg_value("hit_radius_multiplier", 1.0),
        ),
        default=1.0,
    )
    effective_hit_radius = max(1.0, base_hit_radius * max(hit_radius_multiplier, 1e-6))

    sprite_path: Optional[str] = context.cfg_value("sprite")
    sprite_surface = load_image_safe(sprite_path) if sprite_path else None

    return ProjectileParams(
        speed=speed,
        scale_multiplier=scale_multiplier,
        effective_scale=effective_scale,
        hit_radius=effective_hit_radius,
        damage=context.cfg_value("damage", 0),
        lifespan=context.cfg_value("lifespan", 0),
        sprite_path=sprite_path,
        sprite_surface=sprite_surface,
    )


def _resolve_speed(context: SpellReleaseContext) -> float:
    speed = coerce_float(context.cfg_value("speed", 0.0), default=0.0)
    if speed <= 0.0:
        speed = coerce_float(context.context.get("speed", 0.0), default=0.0)
    if speed <= 0.0:
        speed = 1.0
    return speed
