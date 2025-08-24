from __future__ import annotations
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple


@dataclass
class AnimRow:
    state_class: str
    value: Optional[str]  # assigned animation base for current target (None -> inherits)
    inherited: bool = False  # True when coming from default while viewing an override target


@dataclass
class FsmAssigmentAnimationsModel:
    # Visibility and target selection
    visible: bool = False
    target_set_id: str = "default"  # 'default' or a set id
    available_targets: List[str] = field(default_factory=lambda: ["default"])  # populated from runtime sets

    # Data loaded from animation_map.json
    default_map: Dict[str, str] = field(default_factory=dict)
    overrides_map: Dict[str, Dict[str, str]] = field(default_factory=dict)  # by set_id

    # Prepared rows for rendering the current target
    rows: List[AnimRow] = field(default_factory=list)
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
    needs_reload: bool = True  # force initial load from disk


__all__ = [
    "FsmAssigmentAnimationsModel",
    "AnimRow",
]

