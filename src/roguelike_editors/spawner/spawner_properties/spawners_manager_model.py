from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Optional


@dataclass
class SpawnersManagerModel:
    visible: bool = False
    selected_template: Optional[Dict[str, Any]] = None
    scroll_offset: int = 0
    # UI state
    hovered_index: Optional[int] = None
    editing_key: Optional[str] = None  # dotted path like "trigger.radius"
    editing_row_index: Optional[int] = None


__all__ = ["SpawnersManagerModel"]
