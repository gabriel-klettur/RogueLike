from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class ListPanelModel:
    visible: bool = True
    title: str = "Spawners"
    selected_index: Optional[int] = None
    hovered_index: Optional[int] = None
    items: List[str] = field(default_factory=list)
    # Scrolling support
    row_height: int = 20
    header_height: int = 28
    visible_rows: int = 11
    scroll_offset: int = 0


__all__ = ["ListPanelModel"]
