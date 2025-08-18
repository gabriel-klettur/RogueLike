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
    # Reflects whether remove mode is active (used by toolbar view to show blinking border)
    remove_mode_active: bool = False
    # Reflects whether add mode is active (used by toolbar view to show blinking border)
    add_mode_active: bool = False
    # Templates currently available to select in the Add dropdown (ids only)
    add_templates: List[str] = field(default_factory=list)


__all__ = ["SpawnerInstanceToolbarModel", "DEFAULT_BUTTONS"]
