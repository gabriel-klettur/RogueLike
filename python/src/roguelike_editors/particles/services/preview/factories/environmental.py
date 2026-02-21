"""Factories for teleportation and environmental particle previews."""
from __future__ import annotations

from typing import Any, Optional, Sequence, Tuple

from roguelike_editors.spells.services.particle_preview import (
    ParticlePreviewFallingLeaf,
    ParticlePreviewTeleport,
    ParticlePreviewWaterFlow,
    ParticlePreviewWaterFountain,
)
from roguelike_editors.spells.services.particle_previews.combat_misc import ParticlePreviewPortal

from ..context import ParticlePreviewContext
from ..validators import warn_curve, warn_emission


def build_teleport(context: ParticlePreviewContext) -> ParticlePreviewTeleport:
    lifetime = _extract_effect_lifetime(context.definition)
    if isinstance(lifetime, (int, float)):
        cycle_ms = int(max(300, min(900, float(lifetime) * 1000)))
    else:
        cycle_ms = 600
    return ParticlePreviewTeleport(
        color=context.color_or((0, 200, 255)),
        cycle_ms=cycle_ms,
    )


def build_water_fountain(context: ParticlePreviewContext) -> ParticlePreviewWaterFountain:
    parts = context.parts
    spouts = _resolve_spouts(parts.get("spouts"))
    emit_rate = context.get_int("emit_rate", 5) or 5
    speed = context.get_number("speed", 2.0) or 2.0
    gravity = context.get_number("gravity", 0.25) or 0.25
    droplet_size = context.get_int("droplet_size", 2) or 2
    splash_count = context.get_int("splash_count", 2) or 2
    blend_mode = _string_or_none(parts.get("blend_mode"))
    alpha_over_life = context.get_tuple("alpha_over_life")
    size_over_life = context.get_tuple("size_over_life")
    color_over_life = context.get_tuple("color_over_life")
    emission_shape = _string_or_none(parts.get("emission_shape"))
    emission_extent = parts.get("emission_extent")
    speed_variance = context.get_number("speed_variance")

    warn_curve("alpha_over_life", alpha_over_life)
    warn_curve("size_over_life", size_over_life)
    warn_curve("color_over_life", color_over_life)
    warn_emission("water_fountain", emission_shape, emission_extent)

    return ParticlePreviewWaterFountain(
        color=context.color_or((100, 180, 255)),
        spouts=spouts,
        emit_rate=int(emit_rate),
        speed=float(speed),
        gravity=float(gravity),
        droplet_size=int(droplet_size),
        splash_count=int(splash_count),
        blend_mode=blend_mode,
        alpha_over_life=alpha_over_life,
        size_over_life=size_over_life,
        color_over_life=color_over_life,
        emission_shape=emission_shape,
        emission_extent=emission_extent,
        speed_variance=speed_variance,
    )


def build_falling_leaf(context: ParticlePreviewContext) -> ParticlePreviewFallingLeaf:
    parts = context.parts
    interval_ms = _resolve_interval_ms(parts)
    life_ms = _resolve_life_ms(parts)
    speed = context.get_number("speed", 0.5) or 0.5
    gravity = context.get_number("gravity", 0.06) or 0.06
    sway_amp = context.get_number("sway_amp", 0.6) or 0.6
    sway_speed = context.get_number("sway_speed", 0.15) or 0.15
    size = _resolve_size(parts.get("size"))
    blend_mode = _string_or_none(parts.get("blend_mode"))
    alpha_over_life = context.get_tuple("alpha_over_life")
    color_over_life = context.get_tuple("color_over_life")
    lifetime_jitter = context.get_number("lifetime_jitter")
    size_start = _size_start(parts.get("size_start"))

    warn_curve("alpha_over_life", alpha_over_life)
    warn_curve("color_over_life", color_over_life)

    return ParticlePreviewFallingLeaf(
        color=context.color_or((120, 200, 80)),
        interval_ms=int(interval_ms),
        life_ms=int(life_ms),
        speed=float(speed),
        gravity=float(gravity),
        sway_amp=float(sway_amp),
        sway_speed=float(sway_speed),
        size=size,
        blend_mode=blend_mode,
        alpha_over_life=alpha_over_life,
        color_over_life=color_over_life,
        lifetime_jitter=lifetime_jitter,
        size_start=size_start,
    )


def build_water_flow(context: ParticlePreviewContext) -> ParticlePreviewWaterFlow:
    parts = context.parts
    base_color = context.color_or((20, 40, 80))
    highlight_color = _resolve_highlight_color(parts, context)
    direction = _resolve_direction(parts.get("direction"))
    speed = context.get_number("speed", 0.6) or 0.6
    stripe_gap = context.get_int("stripe_gap", 8) or 8
    ripple_amp = context.get_number("ripple_amp", 0.6) or 0.6
    alpha_base = context.get_int("alpha_base", 120) or 120
    alpha_wave = context.get_int("alpha_wave", 80) or 80

    return ParticlePreviewWaterFlow(
        base_color=base_color,
        highlight_color=highlight_color,
        direction=direction,
        speed=float(speed),
        stripe_gap=int(stripe_gap),
        ripple_amp=float(ripple_amp),
        alpha_base=int(alpha_base),
        alpha_wave=int(alpha_wave),
    )


# ---------------------------------------------------------------------------
# Stylized Portal
# ---------------------------------------------------------------------------

def build_portal(context: ParticlePreviewContext) -> ParticlePreviewPortal:
    parts = context.parts
    def _rgb(name: str, fallback: tuple[int, int, int]) -> tuple[int, int, int]:
        v = parts.get(name)
        if isinstance(v, (list, tuple)) and len(v) >= 3:
            try:
                return (int(v[0]), int(v[1]), int(v[2]))
            except Exception:
                return fallback
        return fallback

    rim_color = _rgb("rim_color", (180, 255, 120))
    core_color = _rgb("core_color", (16, 36, 28))
    swirl_color = _rgb("swirl_color", (150, 255, 100))
    ellipse_ratio = context.get_number("ellipse_ratio", 1.8) or 1.8
    outer_radius = context.get_int("outer_radius", 28) or 28
    inner_radius = context.get_int("inner_radius", 14) or 14
    swirl_width = context.get_int("swirl_width", 6) or 6
    chips_count = context.get_int("chips_count", 4) or 4
    angle_speed = context.get_number("angle_speed", 0.8) or 0.8

    return ParticlePreviewPortal(
        rim_color=rim_color,
        core_color=core_color,
        swirl_color=swirl_color,
        ellipse_ratio=float(ellipse_ratio),
        outer_radius=int(outer_radius),
        inner_radius=int(inner_radius),
        swirl_width=int(swirl_width),
        chips_count=int(chips_count),
        angle_speed=float(angle_speed),
    )

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _extract_effect_lifetime(definition: dict[str, Any]) -> Optional[float]:
    effect = definition.get("effect")
    if isinstance(effect, dict):
        lifetime = effect.get("lifetime")
        if isinstance(lifetime, (int, float)):
            return float(lifetime)
    return None


def _resolve_spouts(raw_spouts: Any) -> Sequence[float]:
    if not isinstance(raw_spouts, (list, tuple)) or not raw_spouts:
        return [0.34, 0.5, 0.66]
    result: list[float] = []
    for value in raw_spouts:
        try:
            result.append(float(max(0.05, min(0.95, float(value)))))
        except (TypeError, ValueError):
            continue
    return result or [0.34, 0.5, 0.66]


def _resolve_interval_ms(parts: dict[str, Any]) -> int:
    interval_ms = parts.get("interval_ms")
    if isinstance(interval_ms, int) and interval_ms > 0:
        return interval_ms
    seconds = parts.get("interval_s")
    if isinstance(seconds, (int, float)) and seconds > 0:
        return int(float(seconds) * 1000.0)
    return 30000


def _resolve_life_ms(parts: dict[str, Any]) -> int:
    life_ms = parts.get("life_ms")
    if isinstance(life_ms, int) and life_ms > 0:
        return life_ms
    if isinstance(parts.get("lifespan_ms"), int):
        return int(parts["lifespan_ms"])
    if isinstance(parts.get("lifespan"), int):
        return int(parts["lifespan"]) * 33
    return 6000


def _resolve_size(raw_size: Any) -> Tuple[int, int]:
    if (
        isinstance(raw_size, (list, tuple))
        and len(raw_size) >= 2
        and all(isinstance(v, (int, float)) for v in raw_size[:2])
    ):
        return (int(raw_size[0]), int(raw_size[1]))
    return (3, 2)


def _size_start(value: Any) -> Any:
    if isinstance(value, (int, float)):
        return value
    if isinstance(value, (list, tuple)):
        return value
    return None


def _resolve_highlight_color(parts: dict[str, Any], context: ParticlePreviewContext) -> Tuple[int, int, int]:
    highlight = parts.get("highlight_color")
    if (
        isinstance(highlight, (list, tuple))
        and len(highlight) >= 3
        and all(isinstance(v, (int, float)) for v in highlight[:3])
    ):
        return (int(highlight[0]), int(highlight[1]), int(highlight[2]))

    palette = context.palette
    if palette and len(palette) >= 2:
        return tuple(palette[1])  # type: ignore[return-value]

    return (60, 110, 160)


def _resolve_direction(value: Any) -> Tuple[float, float]:
    if (
        isinstance(value, (list, tuple))
        and len(value) >= 2
        and all(isinstance(v, (int, float)) for v in value[:2])
    ):
        return (float(value[0]), float(value[1]))
    return (1.0, 0.0)


def _string_or_none(value: Any) -> Optional[str]:
    return value if isinstance(value, str) else None
