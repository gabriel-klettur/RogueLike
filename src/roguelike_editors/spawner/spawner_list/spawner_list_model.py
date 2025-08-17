from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class SpawnerListModel:
    visible: bool = True
    selected_index: Optional[int] = None
    hovered_index: Optional[int] = None
    items: List[str] = field(default_factory=list)


__all__ = ["SpawnerListModel"]
