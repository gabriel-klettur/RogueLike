from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class Row:
    """Represents a single editable/display row in the properties list."""
    key: str
    value: str | None
    editable: bool = True


@dataclass
class FsmPropertiesPanelModel:
    """Model for the FSM Properties Panel.

    Tracks visibility, active tab, selected set/node/transition, and the rows to render/edit.
    """
    # Visibility
    visible: bool = False

    # Tabs: 'nodes' or 'transitions'
    active_tab: str = "nodes"

    # Data selection
    selected_set_id: Optional[str] = None
    set_ids: List[str] = field(default_factory=list)

    # When active_tab == 'nodes'
    node_ids: List[str] = field(default_factory=list)
    selected_node_id: Optional[str] = None

    # When active_tab == 'transitions'
    transition_labels: List[str] = field(default_factory=list)
    selected_transition_index: Optional[int] = None

    # Rows for the lower table (key/value pairs)
    rows: List[Row] = field(default_factory=list)

    # UI state: hover/selection/editing
    hovered_index: Optional[int] = None
    selected_index: Optional[int] = None
    editing_index: Optional[int] = None
    editing_text: str = ""

    # Scrolling
    scroll: int = 0
    max_scroll: int = 0

    # Geometry cache (filled by View)
    panel_rect: Optional[object] = None  # pygame.Rect at runtime
    header_rect: Optional[object] = None
    tabs_nodes_rect: Optional[object] = None
    tabs_trans_rect: Optional[object] = None
    set_prev_rect: Optional[object] = None
    set_next_rect: Optional[object] = None
    set_combo_rect: Optional[object] = None
    item_prev_rect: Optional[object] = None
    item_next_rect: Optional[object] = None
    item_combo_rect: Optional[object] = None

    # Column X for value rendering
    value_col_x: int = 200


__all__ = ["FsmPropertiesPanelModel", "Row"]

