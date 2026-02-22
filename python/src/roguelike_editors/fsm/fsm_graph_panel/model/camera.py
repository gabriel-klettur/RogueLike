from __future__ import annotations

from dataclasses import dataclass


@dataclass
class CameraState:
    offset_x: float = 0.0
    offset_y: float = 0.0
    zoom: float = 1.0
