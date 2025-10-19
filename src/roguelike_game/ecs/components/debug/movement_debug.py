from __future__ import annotations

from dataclasses import dataclass
from typing import Optional, Tuple


@dataclass
class MovementDebug:
    """Debug per-entity movement state for rendering purposes.
    Stores last position, last normalized direction and a simple stuck counter.
    """
    last_pos: Optional[Tuple[float, float]] = None
    last_dir: Optional[Tuple[float, float]] = None
    stuck_frames: int = 0
