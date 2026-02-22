from dataclasses import dataclass
from typing import Tuple


@dataclass
class TelegraphArc:
    """Visual telegraph for upcoming cone-based attacks.

    Attributes:
        radius: Target arc radius in world units (pixels) at impact time.
        arc_angle: Arc opening in radians.
        direction: Normalized direction vector (dx, dy).
        color: RGBA color tuple.
        offset: Distance from owner center to arc center along direction.
        progress: 0..1 fraction of radial fill over wind-up time.
    """
    radius: float
    arc_angle: float
    direction: Tuple[float, float]
    color: Tuple[int, int, int, int]
    offset: float = 0.0
    progress: float = 0.0
