from dataclasses import dataclass, field
from typing import Tuple, Set

@dataclass
class HitboxComponent:
    owner: int
    offset: float
    radius: float
    arc_angle: float
    direction: Tuple[float, float]
    lifespan: int
    damage: float
    hit_targets: Set[int] = field(default_factory=set)
    # Opt-in behavior: follow/rotate with owner each frame (used by player slash)
    follow_owner: bool = False
    rotate_with_owner: bool = False