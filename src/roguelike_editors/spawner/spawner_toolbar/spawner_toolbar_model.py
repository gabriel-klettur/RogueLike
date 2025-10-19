from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional

TOOL_SPAWNER_INSTANCES: str = "spawner_instances"
TOOL_SPAWNER_TEMPLATES: str = "spawner_templates"
TOOL_TUTORIAL_SPAWNER: str = "tutorial_spawner"
TOOL_UNDO: str = "undo"
TOOL_REDO: str = "redo"

ICON_PATHS = {
    TOOL_UNDO: 'assets/ui/undo.png',
    TOOL_SPAWNER_INSTANCES: 'assets/ui/spawner_editor/spawner_list.png',
    TOOL_SPAWNER_TEMPLATES: 'assets/ui/spawner_editor/spawner_manager.png',
    TOOL_TUTORIAL_SPAWNER: 'assets/ui/tutorials_button.png',
    TOOL_REDO: 'assets/ui/redo.png',
}

DEFAULT_BUTTONS: List[str] = [
    "spawner_instances",
    "spawner_templates",  # debajo de spawner_instances
    "tutorial_spawner",
    "undo",
    "redo",
]


@dataclass
class SpawnerToolbarModel:
    visible: bool = True
    active_tool: Optional[str] = None
    buttons: List[str] = field(default_factory=lambda: list(DEFAULT_BUTTONS))


__all__ = [
    "SpawnerToolbarModel",
    "DEFAULT_BUTTONS",
    "TOOL_SPAWNER_INSTANCES",
    "TOOL_SPAWNER_TEMPLATES",
    "TOOL_TUTORIAL_SPAWNER",
    "TOOL_UNDO",
    "TOOL_REDO",
    "ICON_PATHS",
]
