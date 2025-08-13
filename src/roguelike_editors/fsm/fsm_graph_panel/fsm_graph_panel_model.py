from __future__ import annotations
from dataclasses import dataclass


@dataclass
class FsmGraphPanelModel:
    visible: bool = True
    pan_x: float = 0.0
    pan_y: float = 0.0
    zoom: float = 1.0


__all__ = ["FsmGraphPanelModel"]
