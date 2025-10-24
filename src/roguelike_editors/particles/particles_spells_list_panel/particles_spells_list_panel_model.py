from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Tuple


@dataclass
class ParticlesSpellsListPanelModel:
    """Model for the spells-usage list panel.

    Stores UI state, selection, and computed usages for the currently
    selected particle preset from the picker.
    """

    # Visibility and layout
    visible: bool = False
    x: int = 0
    y: int = 0
    width: int = 260
    padding: int = 8

    # Expand/collapse state
    expanded: bool = True

    # Selection from picker
    selected_preset_id: str | None = None

    # Computed usages: list of (spell_key, usage_path)
    usages: List[Tuple[str, str]] = field(default_factory=list)

    # Cache to avoid recomputing too frequently
    _last_spells_version: int = -1
    _last_computed_for: str | None = None

