from __future__ import annotations

from dataclasses import dataclass, field
from typing import List

DEFAULT_BUTTONS: List[str] = [
    "add_spawner",
    "remove_spawner",
]


@dataclass
class SpawnerInstanceToolbarModel:
    visible: bool = True
    buttons: List[str] = field(default_factory=lambda: list(DEFAULT_BUTTONS))


__all__ = ["SpawnerInstanceToolbarModel", "DEFAULT_BUTTONS"]
