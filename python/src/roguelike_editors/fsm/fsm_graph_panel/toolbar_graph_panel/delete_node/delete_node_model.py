from __future__ import annotations
from dataclasses import dataclass
from typing import Tuple


@dataclass
class DeleteNodeModel:
    # Visual preferences for delete preview overlay
    node_highlight_color: Tuple[int, int, int] = (255, 140, 140)
    node_outline_width: int = 3
    edge_highlight_color: Tuple[int, int, int] = (240, 90, 90)
    edge_highlight_width: int = 3
    # Picking tolerance in LOCAL (canvas) space pixels for edges
    edge_pick_tolerance: int = 8


__all__ = ["DeleteNodeModel"]
