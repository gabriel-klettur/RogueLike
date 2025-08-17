from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Optional


@dataclass
class SpawnersManagerModel:
    visible: bool = False
    selected_template: Optional[Dict[str, Any]] = None
    scroll_offset: int = 0


__all__ = ["SpawnersManagerModel"]
