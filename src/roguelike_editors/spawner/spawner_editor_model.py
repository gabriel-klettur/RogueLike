from __future__ import annotations

from dataclasses import dataclass, field
from roguelike_editors.spawner.spawner_title.spawner_title_model import SpawnerTitleModel


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
    # Title submodel for consistent title bar rendering
    title_model: SpawnerTitleModel = field(default_factory=SpawnerTitleModel)
    # Pending zone confirmation overlay data, or None
    # {
    #   'eid': int,
    #   'orig_zone': str,
    #   'proposed_zone': str,
    #   'orig_local': tuple[int,int] | None,
    # }
    pending_zone_confirm: dict | None = None
