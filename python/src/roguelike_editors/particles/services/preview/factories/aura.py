"""Factories for aura-style particle previews."""
from __future__ import annotations

from typing import Any, Optional, Sequence, Tuple

from roguelike_editors.spells.services.particle_preview import (
    ParticlePreviewAura,
    ParticlePreviewHealingAura,
)

from ..context import ParticlePreviewContext
from ..validators import warn_curve, warn_emission

NumberTuple = Optional[Tuple[Any, ...]]


def build_aura(context: ParticlePreviewContext) -> ParticlePreviewAura | ParticlePreviewHealingAura:
    parts = context.parts
    radius = context.get_int("radius")
    palette = context.palette_or_none()
    blend_mode = _string_or_none(parts.get("blend_mode"))
    ellipse_ratio = context.get_number("ellipse_ratio", 1.0) or 1.0

    if _has_emitter_parameters(parts):
        return _build_healing_aura(context, radius, palette, blend_mode, ellipse_ratio)
    return _build_generic_aura(context, radius, palette, blend_mode, ellipse_ratio)


# ---------------------------------------------------------------------------
# Internal builders
# ---------------------------------------------------------------------------

def _has_emitter_parameters(parts: dict[str, Any]) -> bool:
    return any(key in parts for key in ("emit_rate", "lifespan", "size_range"))


def _build_healing_aura(
    context: ParticlePreviewContext,
    radius: Optional[int],
    palette: Optional[Sequence[Tuple[int, int, int]]],
    blend_mode: Optional[str],
    ellipse_ratio: float,
) -> ParticlePreviewHealingAura:
    parts = context.parts
    emit_rate = context.get_int("emit_rate", 3) or 3
    emit_rate = max(1, emit_rate)
    speed = context.get_number("speed", 1.0) or 1.0
    lifespan = context.get_int("lifespan", 60) or 60
    size_range = context.get_tuple("size_range") or (4, 8)
    warm_steps = min(24, 6 + emit_rate * 2)
    size_over_life = context.get_tuple("size_over_life")
    alpha_over_life = context.get_tuple("alpha_over_life")
    color_over_life = context.get_tuple("color_over_life")
    emission_shape = _string_or_none(parts.get("emission_shape"))
    emission_extent = parts.get("emission_extent")
    emission_direction = _tuple_or_none(parts.get("emission_direction"))
    angle_spread_deg = context.get_number("emission_angle_spread_deg")
    speed_variance = context.get_number("speed_variance")
    lifetime_jitter = context.get_number("lifetime_jitter")
    size_start = _size_start(parts.get("size_start"))
    bursts = parts.get("bursts") if isinstance(parts.get("bursts"), (list, tuple)) else None

    warn_curve("size_over_life", size_over_life)
    warn_curve("alpha_over_life", alpha_over_life)
    warn_curve("color_over_life", color_over_life)
    warn_emission("aura", emission_shape, emission_extent)

    return ParticlePreviewHealingAura(
        color=context.color_or((80, 200, 120)),
        palette=palette,
        radius=radius,
        emit_rate=int(emit_rate),
        speed=float(speed),
        lifespan=int(lifespan),
        size_range=size_range,
        warm_start_steps=warm_steps,
        blend_mode=blend_mode,
        size_over_life=size_over_life,
        alpha_over_life=alpha_over_life,
        color_over_life=color_over_life,
        emission_shape=emission_shape,
        emission_extent=emission_extent,
        emission_direction=emission_direction,
        emission_angle_spread_deg=angle_spread_deg,
        speed_variance=speed_variance,
        lifetime_jitter=lifetime_jitter,
        size_start=size_start,
        bursts=bursts,
        texture_path=_string_or_none(parts.get("texture_path")),
        flipbook=context.get_dict("flipbook"),
        ellipse_ratio=float(ellipse_ratio),
    )


def _build_generic_aura(
    context: ParticlePreviewContext,
    radius: Optional[int],
    palette: Optional[Sequence[Tuple[int, int, int]]],
    blend_mode: Optional[str],
    ellipse_ratio: float,
) -> ParticlePreviewAura:
    parts = context.parts
    speed = context.get_number("speed", 1.0) or 1.0
    count = context.get_int("count")
    if count is None:
        emit_rate = context.get_int("emit_rate")
        if emit_rate is not None and emit_rate > 0:
            count = max(8, min(40, emit_rate * 8))
        else:
            count = 24
    count = max(1, count)
    ring_layers = context.get_int("ring_layers", 1) or 1
    layer_spread = context.get_number("layer_spread", 0.3) or 0.3
    fill_core = bool(parts.get("fill_core"))
    core_fill_alpha = context.get_int("core_fill_alpha", 190) or 190
    core_fill_inner_ratio = context.get_number("core_fill_inner_ratio", 0.6) or 0.6

    return ParticlePreviewAura(
        color=context.color_or((80, 200, 120)),
        radius=radius,
        speed=float(speed),
        count=int(count),
        palette=palette,
        blend_mode=blend_mode,
        ellipse_ratio=float(ellipse_ratio),
        ring_layers=int(ring_layers),
        layer_spread=float(layer_spread),
        fill_core=bool(fill_core),
        core_fill_alpha=int(core_fill_alpha),
        core_fill_inner_ratio=float(core_fill_inner_ratio),
    )


# ---------------------------------------------------------------------------
# Utilities
# ---------------------------------------------------------------------------

def _string_or_none(value: Any) -> Optional[str]:
    return value if isinstance(value, str) else None


def _tuple_or_none(value: Any) -> Optional[Tuple[float, float]]:
    if (
        isinstance(value, (list, tuple))
        and len(value) >= 2
        and all(isinstance(v, (int, float)) for v in value[:2])
    ):
        return (float(value[0]), float(value[1]))
    return None


def _size_start(value: Any) -> Any:
    if isinstance(value, (int, float)):
        return value
    if isinstance(value, (list, tuple)):
        return value
    return None
