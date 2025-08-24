from __future__ import annotations
from dataclasses import dataclass
from typing import Tuple


@dataclass
class DisconnectModel:
    # Visual preferences for the preview overlay of the disconnect tool
    preview_color: Tuple[int, int, int] = (220, 120, 120)
    arrow_head_len: int = 14
    arrow_head_width: int = 10


__all__ = ["DisconnectModel"]
