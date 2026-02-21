from __future__ import annotations

from dataclasses import dataclass, field
from typing import List


@dataclass
class ActionGridModel:
    """State of the Action Grid: page, items, layout and transient flags."""
    rows: int = 3
    cols: int = 10
    page: int = 0
    items: List[str] = field(default_factory=list)
    minimized: bool = False

    def pages(self) -> int:
        total_slots = self.cols * self.rows
        if total_slots <= 2:
            return 0
        visible_slots = max(1, total_slots - 2)
        return (len(self.items) + visible_slots - 1) // visible_slots
