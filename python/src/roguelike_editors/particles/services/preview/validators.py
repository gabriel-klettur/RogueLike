"""Validation utilities for particle preview configuration."""
from __future__ import annotations

import logging
from typing import Iterable, Optional, Sequence

logger = logging.getLogger(__name__)


def warn_curve(name: str, curve: Optional[Sequence[Sequence[float]]]) -> None:
    """Log a warning if the provided curve has invalid time keys."""

    if not isinstance(curve, Iterable):  # type: ignore[arg-type]
        return

    last_t = float("-inf")
    has_issue = False
    for point in curve:  # type: ignore[var-annotated]
        if not isinstance(point, Sequence) or not point:
            has_issue = True
            continue
        try:
            t = float(point[0])
        except (TypeError, ValueError):
            has_issue = True
            continue
        if not 0.0 <= t <= 1.0 or t < last_t:
            has_issue = True
        last_t = t

    if has_issue:
        logger.warning(
            "[particles.preview] curve '%s' has unsorted/out-of-range keys; expected t in [0,1] ascending",
            name,
        )


def warn_emission(kind: str, shape: Optional[str], extent: Optional[Sequence[float] | float | int]) -> None:
    """Log a warning when the emission shape/extents look suspicious."""

    if not isinstance(shape, str):
        return

    shape_lower = shape.lower()
    known = {"point", "circle", "ring", "line", "box", "cone", "mesh"}
    if shape_lower not in known:
        logger.warning(
            "[particles.preview] unknown emission_shape '%s' for kind=%s",
            shape,
            kind,
        )
        return

    if shape_lower == "mesh":
        logger.warning(
            "[particles.preview] emission_shape 'mesh' not simulated in preview; falling back to default distribution",
        )

    try:
        if shape_lower in ("circle", "cone"):
            if isinstance(extent, (int, float)) and float(extent) < 0:
                logger.warning("[particles.preview] negative extent radius for %s", shape_lower)
        if shape_lower == "ring" and isinstance(extent, Sequence) and len(extent) >= 2:
            inner, outer = float(extent[0]), float(extent[1])
            if inner > outer:
                logger.warning("[particles.preview] ring extent inner>outer; values=%s", extent)
        if shape_lower == "box" and isinstance(extent, Sequence) and len(extent) >= 2:
            width, height = float(extent[0]), float(extent[1])
            if width <= 0 or height <= 0:
                logger.warning("[particles.preview] non-positive box extent; values=%s", extent)
    except (TypeError, ValueError):
        return


__all__ = ["warn_curve", "warn_emission"]
