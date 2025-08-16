from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class FsmSetsPanelModel:
    visible: bool = False
    selected_index: Optional[int] = None
    hovered_index: Optional[int] = None
    # Button hover state
    hovered_button_row: Optional[int] = None
    hovered_button_kind: Optional[str] = None  # 'clone' or 'delete'
    items: List[str] = field(default_factory=list)  # list of set ids


__all__ = ["FsmSetsPanelModel"]
