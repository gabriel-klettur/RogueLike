from __future__ import annotations

from typing import Any

ZOOM_MIN = 0.2
ZOOM_MAX = 3.0


def apply_zoom_at_point(model: Any, local_x: float, local_y: float, factor: float,
                        *, min_zoom: float = ZOOM_MIN, max_zoom: float = ZOOM_MAX) -> bool:
    """Apply zoom around a local canvas point, keeping that world point fixed.
    Returns True if zoom changed the model state.
    """
    try:
        old_z = max(0.05, float(getattr(model, 'zoom', 1.0)))
    except Exception:
        old_z = 1.0
    if factor is None:
        return False
    try:
        f = float(factor)
    except Exception:
        return False
    if abs(f - 1.0) < 1e-9:
        return False

    new_z = max(min_zoom, min(max_zoom, old_z * f))
    if abs(new_z - old_z) < 1e-6:
        return False

    try:
        pan_x = float(getattr(model, 'pan_x', 0.0))
    except Exception:
        pan_x = 0.0
    try:
        pan_y = float(getattr(model, 'pan_y', 0.0))
    except Exception:
        pan_y = 0.0

    wx = (float(local_x) - pan_x) / old_z
    wy = (float(local_y) - pan_y) / old_z

    try:
        model.zoom = new_z
        model.pan_x = float(local_x) - wx * new_z
        model.pan_y = float(local_y) - wy * new_z
    except Exception:
        return False

    return True


def apply_zoom_at_canvas_center(model: Any, canvas_rect: Any, factor: float,
                                *, min_zoom: float = ZOOM_MIN, max_zoom: float = ZOOM_MAX) -> bool:
    """Apply zoom around the canvas center.
    Returns True if zoom changed the model state.
    """
    if canvas_rect is None:
        return False
    try:
        cx = int(canvas_rect.left) + int(canvas_rect.w) // 2
        cy = int(canvas_rect.top) + int(canvas_rect.h) // 2
        local_x = cx - int(canvas_rect.left)
        local_y = cy - int(canvas_rect.top)
    except Exception:
        return False
    return apply_zoom_at_point(model, local_x, local_y, factor, min_zoom=min_zoom, max_zoom=max_zoom)
