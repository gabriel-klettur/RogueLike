from __future__ import annotations
from dataclasses import dataclass
from typing import Tuple


@dataclass
class ConnectModel:
    # Visual preferences for the preview overlay
    preview_color: Tuple[int, int, int] = (255, 230, 120)
    arrow_head_len: int = 14
    arrow_head_width: int = 10


__all__ = ["ConnectModel"]
