"""Graph layout helpers (skeleton)."""
from __future__ import annotations
from typing import Tuple


def snap_to_grid(x: float, y: float, grid: int = 20) -> Tuple[int, int]:
    gx = int(round(x / grid) * grid)
    gy = int(round(y / grid) * grid)
    return gx, gy


def auto_layout(states: list, transitions: list) -> None:
    """In-place naive layout for initial positioning. TODO: implement."""
    return


__all__ = ["snap_to_grid", "auto_layout"]
