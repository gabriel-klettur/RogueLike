from __future__ import annotations

from dataclasses import dataclass
from typing import Optional, Tuple


@dataclass
class DragState:
    kind: Optional[str] = None  # e.g., 'node', 'edge_handle', 'canvas'
    id: Optional[str] = None
    start_screen: Optional[Tuple[int, int]] = None
