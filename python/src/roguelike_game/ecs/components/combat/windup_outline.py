from dataclasses import dataclass
from typing import Tuple


@dataclass
class WindupOutline:
    """Marker component to render collider outlines during melee wind-up.

    Attributes:
        color: RGBA color for the outline.
        width: Line width in pixels.
    """
    color: Tuple[int, int, int, int] = (255, 255, 0, 200)  # Yellow, semi-opaque
    width: int = 2
