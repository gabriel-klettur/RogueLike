from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Optional


DEFAULT_BUTTONS: List[str] = [    
    "sets_list",
    "sets_entities_assignment",
    "sets_animation_assignment",
    "set_properties",
    "undo",
    "redo",
    "tutorial_fsm",
]


@dataclass
class FsmToolbarModel:
    visible: bool = True
    active_tool: Optional[str] = None
    buttons: List[str] = field(default_factory=lambda: list(DEFAULT_BUTTONS))


__all__ = ["FsmToolbarModel", "DEFAULT_BUTTONS"]
