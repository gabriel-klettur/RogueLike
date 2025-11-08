from __future__ import annotations

from dataclasses import dataclass
from typing import Tuple, Optional
import math
import pygame

Color = Tuple[int, int, int]


@dataclass
class Light:
    """Point light definition in world coordinates.

    radius: in pixels at zoom=1.0.
    intensity: 0..1 scales the color contribution.
    flicker_amp: amplitude (0..1) of intensity modulation.
    flicker_speed: speed factor for flicker (Hz approx).
    """
    x: float
    y: float
    radius: int
    color: Color = (255, 220, 180)
    intensity: float = 1.0
    falloff: float = 2.0  # exponent for radial fade
    enabled: bool = True
    flicker_amp: float = 0.0
    flicker_speed: float = 2.3
    flicker_phase_rad: float = 0.0
    center_scale: float = 1.0
    id: Optional[str] = None

    def current_intensity(self) -> float:
        if self.flicker_amp <= 0.0:
            return max(0.0, min(1.0, float(self.intensity)))
        t = pygame.time.get_ticks() / 1000.0
        # Per-light phase offset to avoid synchronized flicker across lights
        mod = 0.5 + 0.5 * math.sin(t * self.flicker_speed * 2.0 * math.pi + float(self.flicker_phase_rad))
        return max(0.0, min(1.0, float(self.intensity) * (1.0 - self.flicker_amp + self.flicker_amp * mod)))
