"""Factories for directional and melee particle previews (dash, slash, lightning, laser)."""
from __future__ import annotations

from typing import Any, Optional, Sequence

from roguelike_editors.spells.services.particle_preview import (
    ParticlePreviewDash,
    ParticlePreviewLaser,
    ParticlePreviewLightning,
    ParticlePreviewSlash,
)

from ..context import ParticlePreviewContext
from ..validators import warn_curve


def build_dash(context: ParticlePreviewContext) -> ParticlePreviewDash:
    speed_px = context.get_number("speed_px", 60.0) or 60.0
    blend_mode = _string_or_none(context.parts.get("blend_mode"))
    return ParticlePreviewDash(
        color=context.color_or((180, 220, 255)),
        speed_px=float(speed_px),
        blend_mode=blend_mode,
    )


def build_slash(context: ParticlePreviewContext) -> ParticlePreviewSlash:
    speed = context.get_number("speed", 2.5) or 2.5
    blend_mode = _string_or_none(context.parts.get("blend_mode"))
    return ParticlePreviewSlash(
        color=context.color_or((100, 220, 255)),
        speed=float(speed),
        blend_mode=blend_mode,
    )


def build_laser(context: ParticlePreviewContext) -> ParticlePreviewLaser:
    blend_mode = _string_or_none(context.parts.get("blend_mode"))
    return ParticlePreviewLaser(
        color=context.color_or((0, 255, 255)),
        blend_mode=blend_mode,
    )


def build_lightning(context: ParticlePreviewContext) -> ParticlePreviewLightning:
    parts = context.parts
    segments = context.get_int("segments", 10) or 10
    offset = context.get_int("offset", 10) or 10
    lifetime = context.get_int("lifetime", 8) or 8
    thickness = context.get_int("thickness", 2) or 2
    blend_mode = _string_or_none(parts.get("blend_mode"))
    alpha_over_life = context.get_tuple("alpha_over_life")
    color_over_life = context.get_tuple("color_over_life")

    warn_curve("alpha_over_life", alpha_over_life)
    warn_curve("color_over_life", color_over_life)

    return ParticlePreviewLightning(
        color=context.color_or((120, 200, 255)),
        segments=segments,
        offset=offset,
        lifetime=lifetime,
        thickness=thickness,
        blend_mode=blend_mode,
        alpha_over_life=alpha_over_life,
        color_over_life=color_over_life,
    )


def _string_or_none(value: Any) -> Optional[str]:
    return value if isinstance(value, str) else None
