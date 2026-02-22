from __future__ import annotations
from dataclasses import dataclass
from typing import Tuple


@dataclass
class CloneModel:
    # Visual preferences for clone preview overlay
    preview_color: Tuple[int, int, int] = (160, 210, 255)
    node_outline_width: int = 2
    # Default clone offset in world units (same units as node x/y)
    offset_dx: int = 20
    offset_dy: int = 20


__all__ = ["CloneModel"]
