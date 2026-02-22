"""Factories for smoke-based particle previews."""
from __future__ import annotations

from typing import Any, Optional, Tuple

from roguelike_editors.spells.services.particle_preview import (
    ParticlePreviewSmoke,
    ParticlePreviewSmokeBurst,
)

from ..context import ParticlePreviewContext

OptionalTuple = Optional[Tuple[Any, ...]]


def build_smoke_emitter(context: ParticlePreviewContext) -> ParticlePreviewSmoke:
    parts = context.parts
    emit_rate = _resolve_emit_rate(parts)
    speed = context.get_number("speed", 1.0) or 1.0
    lifespan = context.get_number("lifespan", 100.0) or 100.0
    size_range = _resolve_size_range(context.get_tuple("size_range"))
    dispersion = _resolve_dispersion(context.get_number("dispersion"))
    warm_steps = min(24, 6 + emit_rate * 2)
    palette = context.palette_or_none()
    gravity = _resolve_gravity(parts.get("gravity"))
    drag = context.get_number("drag")
    blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
    sol = context.get_tuple("size_over_life")
    aol = context.get_tuple("alpha_over_life")
    col = context.get_tuple("color_over_life")

    return ParticlePreviewSmoke(
        color=context.color_or((200, 200, 200)),
        emit_rate=emit_rate,
        warm_start_steps=warm_steps,
        palette=palette,
        speed=float(speed),
        lifespan=float(lifespan),
        size_range=size_range,
        dispersion=dispersion,
        gravity=gravity,
        drag=float(drag) if isinstance(drag, (int, float)) else None,
        blend_mode=blend_mode,
        size_over_life=sol,
        alpha_over_life=aol,
        color_over_life=col,
        texture_path=_string_or_none(parts.get("texture_path")),
        flipbook=context.get_dict("flipbook"),
        speed_variance=context.get_number("speed_variance"),
        lifetime_jitter=context.get_number("lifetime_jitter"),
        size_start=_coerce_size_start(parts.get("size_start")),
    )


def build_smoke_burst(context: ParticlePreviewContext) -> ParticlePreviewSmokeBurst:
    parts = context.parts
    count = context.get_int("count", 12) or 12
    count = max(1, min(40, count))
    direction = _resolve_direction(parts.get("direction"))
    warm_steps = min(18, 6 + count // 4)
    blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None

    return ParticlePreviewSmokeBurst(
        color=context.color_or((200, 200, 200)),
        count=int(count),
        direction=direction,
        warm_start_steps=warm_steps,
        blend_mode=blend_mode,
        texture_path=_string_or_none(parts.get("texture_path")),
        flipbook=context.get_dict("flipbook"),
    )


def build_default_smoke(context: ParticlePreviewContext) -> ParticlePreviewSmoke:
    emit_rate = context.get_int("emit_rate", 2) or 2
    return ParticlePreviewSmoke(
        color=context.color_or((200, 200, 200)),
        emit_rate=max(1, emit_rate),
    )


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _resolve_emit_rate(parts: dict[str, Any]) -> int:
    emit_rate = parts.get("emit_rate")
    if isinstance(emit_rate, int) and emit_rate > 0:
        return emit_rate

    count = parts.get("count")
    if isinstance(count, int) and count > 0:
        return max(1, min(8, count // 2))
    return 2


def _resolve_size_range(value: OptionalTuple) -> OptionalTuple:
    if value is None or len(value) < 2:
        return None
    return tuple(value[:2])


def _resolve_dispersion(value: Optional[float]) -> float:
    if isinstance(value, (int, float)):
        return float(value) * 0.025
    return 0.3


def _resolve_gravity(value: Any) -> Optional[Tuple[float, float]]:
    if isinstance(value, (int, float)):
        return (0.0, float(value))
    if (
        isinstance(value, (list, tuple))
        and len(value) >= 2
        and all(isinstance(v, (int, float)) for v in value[:2])
    ):
        return (float(value[0]), float(value[1]))
    return None


def _resolve_direction(value: Any) -> Tuple[float, float]:
    if (
        isinstance(value, (list, tuple))
        and len(value) >= 2
        and all(isinstance(v, (int, float)) for v in value[:2])
    ):
        return (float(value[0]), float(value[1]))
    return (0.0, -1.0)


def _string_or_none(value: Any) -> Optional[str]:
    return value if isinstance(value, str) else None


def _coerce_size_start(value: Any) -> Any:
    if isinstance(value, (int, float)):
        return value
    if isinstance(value, (list, tuple)):
        return value
    return None
