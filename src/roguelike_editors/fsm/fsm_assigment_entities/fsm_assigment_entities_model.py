from __future__ import annotations
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple


@dataclass
class EntityAssignRow:
    key: str  # archetype name or eid string
    value: Optional[str]  # assigned set id


@dataclass
class FsmAssigmentEntitiesModel:
    # Visibility and target selection
    visible: bool = False
    target_category: str = "by_archetype"  # 'by_archetype' or 'by_eid'
    available_targets: List[str] = field(default_factory=lambda: ["by_archetype", "by_eid"])

    # Data loaded from assignments.json
    by_archetype: Dict[str, str] = field(default_factory=dict)
    by_eid: Dict[str, str] = field(default_factory=dict)

    # Prepared rows for rendering the current category
    rows: List[EntityAssignRow] = field(default_factory=list)
    selected_index: Optional[int] = None
    hovered_index: Optional[int] = None

    # Inline editing state
    editing_index: Optional[int] = None
    editing_text: str = ""

    # Layout and interaction
    panel_rect: Optional[Tuple[int, int, int, int]] = None
    scroll: int = 0
    max_scroll: int = 0

    # Dirty flags
    needs_reload: bool = True


__all__ = [
    "FsmAssigmentEntitiesModel",
    "EntityAssignRow",
]

