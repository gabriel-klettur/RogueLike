"""Factories for explosive and projectile-like particle previews."""
from __future__ import annotations

from typing import Any, Optional, Sequence, Tuple

from roguelike_editors.spells.services.particle_preview import (
    ParticlePreviewArcaneFlame,
    ParticlePreviewExplosion,
    ParticlePreviewFirework,
)

from ..context import ParticlePreviewContext
from ..validators import warn_curve


def build_firework(context: ParticlePreviewContext) -> ParticlePreviewFirework:
    speed = context.get_number("speed", 12.0) or 12.0
    return ParticlePreviewFirework(
        color=context.color if context.color_explicit else None,
        speed=float(speed),
    )


def build_arcane_flame(context: ParticlePreviewContext) -> ParticlePreviewArcaneFlame:
    definition = context.definition
    duration = _extract_duration_seconds(definition)
    seed = context.get_int("seed", 0) or 0
    count = context.get_int("count", 20) or 20
    spark_rate = max(2, min(14, int(count * 0.5)))
    speed = context.get_number("speed", 100.0) or 100.0
    spark_speed = max(0.6, min(2.5, float(speed) / 90.0))
    life = context.get_int("lifespan", 60) or 60
    spark_life = max(12, min(60, int(life * 0.5)))
    size_range = _resolve_size_range(context.get_tuple("size_range"))

    return ParticlePreviewArcaneFlame(
        duration=duration,
        seed=int(seed),
        spark_rate=int(spark_rate),
        spark_speed=float(spark_speed),
        spark_size_range=size_range,
        spark_lifespan=int(spark_life),
    )


def build_explosion(context: ParticlePreviewContext) -> ParticlePreviewExplosion:
    parts = context.parts
    palette = context.palette_or_none()
    count = context.get_int("count", 24) or 24
    speed_range = _resolve_speed_range(context.get_number("speed"))
    blend_mode = _string_or_none(parts.get("blend_mode"))
    size_over_life = context.get_tuple("size_over_life")
    alpha_over_life = context.get_tuple("alpha_over_life")
    color_over_life = context.get_tuple("color_over_life")

    warn_curve("size_over_life", size_over_life)
    warn_curve("alpha_over_life", alpha_over_life)
    warn_curve("color_over_life", color_over_life)

    return ParticlePreviewExplosion(
        color=context.color_or((255, 180, 60)),
        palette=palette,
        count=int(count),
        speed_range=speed_range,
        blend_mode=blend_mode,
        size_over_life=size_over_life,
        alpha_over_life=alpha_over_life,
        color_over_life=color_over_life,
    )


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _extract_duration_seconds(definition: dict[str, Any]) -> float:
    effect = definition.get("effect")
    if isinstance(effect, dict):
        duration = effect.get("duration")
        if isinstance(duration, (int, float)):
            return float(duration)
    return 5.0


def _resolve_size_range(size_range: Optional[Tuple[Any, ...]]) -> Tuple[int, int]:
    if not size_range or len(size_range) < 2:
        return (2, 6)
    minimum = max(1, min(3, int(size_range[0])))
    maximum = max(minimum, min(4, int(size_range[1])))
    return (minimum, maximum)


def _resolve_speed_range(speed: Optional[float]) -> Tuple[float, float]:
    if isinstance(speed, (int, float)):
        low = max(0.6, float(speed) * 0.012)
        high = max(low + 0.4, float(speed) * 0.024)
        return (low, high)
    return (0.8, 2.5)


def _string_or_none(value: Any) -> Optional[str]:
    return value if isinstance(value, str) else None
