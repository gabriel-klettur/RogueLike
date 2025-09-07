from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, Optional, Tuple


@dataclass
class InstancePropertiesModel:
    visible: bool = False
    selected_instance: Optional[Dict[str, Any]] = None
    # Index of the instance in spawners_instances.json at the time of selection
    selected_index: Optional[int] = None
    # Original instance id (if present) to aid in persistence by id
    original_id: Optional[str] = None
    # Original identity tuple to locate the entry if index changes
    original_key: Optional[Tuple[str, str, Tuple[int, int]]] = None

    # UI state
    scroll_offset: int = 0
    hovered_index: Optional[int] = None
    editing_key: Optional[str] = None  # dotted path like "overrides.trigger.radius" or "tile.0"
    editing_row_index: Optional[int] = None

    # Combobox state for template_id
    template_combo_open: bool = False
    template_options: list[str] = field(default_factory=list)
    template_hovered_index: Optional[int] = None
    template_scroll_offset: int = 0

    # Visuals (read-only table)
    # Raw visuals dict from the selected spawner instance (e.g., {"AwaitTrigger": 113, ...})
    visuals: Dict[str, Any] = field(default_factory=dict)
    # Prebuilt rows for rendering: (state, building_instance_id, template_id_str)
    visuals_rows: list[tuple[str, str, str]] = field(default_factory=list)

    # Visuals editing state
    visuals_hovered_index: Optional[int] = None
    visuals_editing_state: Optional[str] = None  # which state key is being edited
    visuals_pending_templates: Dict[str, str] = field(default_factory=dict)  # state -> pending template str
