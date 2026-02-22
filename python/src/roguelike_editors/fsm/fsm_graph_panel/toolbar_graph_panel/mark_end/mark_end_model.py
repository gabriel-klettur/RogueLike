from __future__ import annotations
from dataclasses import dataclass
from typing import Tuple


@dataclass
class MarkEndModel:
    # Visual preferences for the hover overlay when marking an end node
    node_highlight_color: Tuple[int, int, int] = (200, 200, 255)
    node_outline_width: int = 3


__all__ = ["MarkEndModel"]
