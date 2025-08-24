from __future__ import annotations

from typing import Any, Tuple


def to_world(model: Any, lx: float, ly: float) -> Tuple[float, float]:
    """Convert local canvas coords (lx, ly) to world coords using model pan/zoom."""
    try:
        z = max(0.05, float(getattr(model, 'zoom', 1.0)))
    except Exception:
        z = 1.0
    try:
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
    except Exception:
        pan_x, pan_y = 0.0, 0.0
    return ((float(lx) - pan_x) / z, (float(ly) - pan_y) / z)


def begin_pan(model: Any, local_x: int, local_y: int) -> None:
    """Begin middle-mouse panning, tracking last local mouse position."""
    model.dragging_pan = True
    model.drag_last_local_x = int(local_x)
    model.drag_last_local_y = int(local_y)


def update_pan(model: Any, local_x: int, local_y: int) -> Tuple[int, int]:
    """Update pan using current local mouse position, returning dx, dy applied."""
    try:
        last_x = int(getattr(model, 'drag_last_local_x', int(local_x)))
        last_y = int(getattr(model, 'drag_last_local_y', int(local_y)))
    except Exception:
        last_x, last_y = int(local_x), int(local_y)
    dx = int(local_x) - last_x
    dy = int(local_y) - last_y
    try:
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
    except Exception:
        pan_x, pan_y = 0.0, 0.0
    model.pan_x = pan_x + dx
    model.pan_y = pan_y + dy
    model.drag_last_local_x = int(local_x)
    model.drag_last_local_y = int(local_y)
    return dx, dy


def end_pan(model: Any) -> None:
    """End middle-mouse panning."""
    model.dragging_pan = False
