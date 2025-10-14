from __future__ import annotations

from typing import Any
import pygame


def get_surface(surf: pygame.Surface) -> pygame.Surface:
    """Return a pygame.Surface compatible object for blitting.

    Some pygame builds expose an internal attribute `_surf`. This helper
    normalizes access for safe blitting across versions.
    """
    return getattr(surf, "_surf", surf)


def clamp(value: float | int, low: float | int, high: float | int) -> float | int:
    """Clamp a numeric value to [low, high]."""
    if low > high:
        low, high = high, low
    if value < low:
        return low
    if value > high:
        return high
    return value
