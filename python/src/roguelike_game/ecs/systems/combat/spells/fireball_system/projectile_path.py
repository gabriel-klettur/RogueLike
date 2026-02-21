"""Utilities to derive projectile sampling data."""
from __future__ import annotations

from typing import Sequence, Tuple

import pygame

from .runtime import FireballRuntime


def ensure_sampling(runtime: FireballRuntime) -> None:
    """Ensure the runtime has sample points and a path AABB."""

    if runtime.sample_points and runtime.path_aabb is not None:
        return

    from .runtime import compute_sampling  # Local import to avoid cycles.

    compute_sampling(runtime)


def sample_circle_rects(sample_points: Sequence[Tuple[float, float]], radius: float) -> list[pygame.Rect]:
    """Return bounding rects for circle samples along the trajectory."""

    rects: list[pygame.Rect] = []
    for sx, sy in sample_points:
        rects.append(
            pygame.Rect(
                int(sx - radius),
                int(sy - radius),
                int(2 * radius) + 1,
                int(2 * radius) + 1,
            )
        )
    return rects
