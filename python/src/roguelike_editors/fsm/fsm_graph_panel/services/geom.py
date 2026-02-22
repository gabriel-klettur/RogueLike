from __future__ import annotations

from typing import Tuple, Any


def rect_contains(rect: Any, x: float, y: float) -> bool:
    """Return True if point (x, y) lies within rect. Supports pygame.Rect or (x,y,w,h)."""
    if hasattr(rect, "collidepoint"):
        try:
            return bool(rect.collidepoint(x, y))
        except Exception:
            pass
    try:
        rx, ry, rw, rh = rect  # type: ignore[misc]
        return (rx <= x < rx + rw) and (ry <= y < ry + rh)
    except Exception:
        return False
