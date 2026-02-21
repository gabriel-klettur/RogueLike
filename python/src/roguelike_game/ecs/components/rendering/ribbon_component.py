from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Tuple, Optional
import time


@dataclass
class RibbonPoint:
    x: float
    y: float
    t_spawn: float


@dataclass
class RibbonComponent:
    """Ribbon-like trail attached to an entity.

    Fields:
      - max_points: maximum stored points in the trail
      - min_distance: minimum distance between successive stored points
      - width_px: base width of the ribbon in pixels
      - color: RGB base color (used when no texture_path)
      - alpha: base alpha (0..255)
      - life_time: optional seconds to keep points alive (fade oldest)
      - texture_path: optional path to a texture to stretch along segments
      - blend_mode: 'alpha' | 'additive' (alpha by default)
    """
    max_points: int = 32
    min_distance: float = 2.0
    width_px: int = 8
    color: Tuple[int, int, int] = (255, 255, 255)
    alpha: int = 200
    life_time: Optional[float] = None
    texture_path: Optional[str] = None
    blend_mode: Optional[str] = None

    points: List[RibbonPoint] = field(default_factory=list)
    _last_x: Optional[float] = None
    _last_y: Optional[float] = None

    def add_point(self, x: float, y: float) -> None:
        now = time.time()
        if self._last_x is not None and self._last_y is not None:
            dx = x - self._last_x
            dy = y - self._last_y
            if (dx * dx + dy * dy) ** 0.5 < float(self.min_distance):
                return
        self.points.append(RibbonPoint(x, y, now))
        self._last_x, self._last_y = x, y
        # Trim by max points
        if len(self.points) > int(self.max_points):
            overflow = len(self.points) - int(self.max_points)
            if overflow > 0:
                self.points = self.points[overflow:]
        # Trim by life time if set
        if isinstance(self.life_time, (int, float)) and self.life_time > 0:
            cut = now - float(self.life_time)
            self.points = [p for p in self.points if p.t_spawn >= cut]
