from __future__ import annotations

from dataclasses import dataclass


@dataclass
class SpawnerEditorModel:
    """Minimal model to drive the Spawner Editor.

    - visible: whether the editor is active
    - dragging: whether RMB drag is active
    - dragging_eid: current spawner entity being dragged
    """
    visible: bool = False
    dragging: bool = False
    dragging_eid: int | None = None
    hovered_eid: int | None = None
