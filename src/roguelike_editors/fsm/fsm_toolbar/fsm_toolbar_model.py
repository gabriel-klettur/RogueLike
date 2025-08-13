from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Optional


DEFAULT_BUTTONS: List[str] = [
    "select",
    "connect",
    "delete",
    "zoom_in",
    "zoom_out",
    "undo",
    "redo",
    "sets",
]


@dataclass
class FsmToolbarModel:
    visible: bool = True
    active_tool: Optional[str] = None
    buttons: List[str] = field(default_factory=lambda: list(DEFAULT_BUTTONS))


__all__ = ["FsmToolbarModel", "DEFAULT_BUTTONS"]
