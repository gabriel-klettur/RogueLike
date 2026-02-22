from __future__ import annotations

from bisect import bisect_left, bisect_right
from typing import Sequence

# Discrete zoom scales used across the project to avoid tile seams/artifacts.
ALLOWED_ZOOMS: list[float] = [0.5, 1.0, 1.5, 1.75, 2.0]


def next_allowed_zoom(current: float, direction: int, allowed: Sequence[float] | None = None) -> float:
    """Return the next allowed zoom value from the list in the given direction.

    direction > 0 -> next higher scale
    direction < 0 -> next lower scale
    direction == 0 -> returns current snapped to the nearest allowed (towards lower)
    """
    allowed = list(allowed) if allowed is not None else ALLOWED_ZOOMS
    if not allowed:
        return float(current) if current else 1.0
    z = float(current) if current else 1.0
    if direction > 0:
        idx = bisect_right(allowed, z)
        return allowed[min(idx, len(allowed) - 1)]
    elif direction < 0:
        idx = bisect_left(allowed, z) - 1
        return allowed[max(idx, 0)]
    # Snap to nearest lower/equal by default
    idx = bisect_left(allowed, z)
    if idx < len(allowed) and allowed[idx] == z:
        return z
    return allowed[max(idx - 1, 0)]
