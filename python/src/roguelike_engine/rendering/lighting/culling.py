from __future__ import annotations

from typing import Tuple


def light_intersects_screen(
    lx: float,
    ly: float,
    radius: int,
    camera,
    screen_size: Tuple[int, int],
    zoom: float | None = None,
    margin: int = 0,
) -> bool:
    """Return True if the light's screen-space bounds intersect the screen.

    - Transforms world (lx, ly) to screen using camera.apply.
    - Scales radius by camera.zoom if provided.
    - Adds optional margin pixels.
    """
    try:
        sx, sy = camera.apply((lx, ly))
    except Exception:
        # If camera missing, assume always visible
        sx, sy = int(lx), int(ly)
    try:
        z = float(getattr(camera, "zoom", 1.0) if zoom is None else zoom)
    except Exception:
        z = 1.0
    rr = int(max(1, radius) * z)
    rr += int(max(0, margin))
    w, h = screen_size
    return not (
        (sx + rr) < 0 or (sy + rr) < 0 or (sx - rr) > w or (sy - rr) > h
    )
