from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional

DEFAULT_BUTTONS: List[str] = [
    "spawner_list",
    "spawner_manager",  # debajo de spawner_list
    "undo",
    "redo",
]


@dataclass
class SpawnerToolbarModel:
    visible: bool = True
    active_tool: Optional[str] = None
    buttons: List[str] = field(default_factory=lambda: list(DEFAULT_BUTTONS))


__all__ = ["SpawnerToolbarModel", "DEFAULT_BUTTONS"]
