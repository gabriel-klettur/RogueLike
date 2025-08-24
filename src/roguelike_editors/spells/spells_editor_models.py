from dataclasses import dataclass, field
from typing import Dict, Any, Optional
import pygame


@dataclass
class SpellsEditorModel:
    """Global state for the Spells Editor.

    Mirrors the ItemsEditorModel structure where applicable and serves as the
    single source-of-truth for top-level editor state. The picker panel keeps
    its own internal model; we bridge key fields between both models.
    """

    # Data stores
    spells: Dict[str, Any]
    assets: Dict[str, pygame.Surface]

    # Visibility and modes
    visible: bool = False
    picker_visible: bool = False
    delete_mode_active: bool = False

    # Selection/hover shared across panels
    selected_id: Optional[str] = None
    hovered_id: Optional[str] = None

    # Optional UI helpers/anchors (used by views and layout)
    title_rect: Optional[pygame.Rect] = None
    picker_left_anchor_x: Optional[int] = None
    top_anchor_y: Optional[int] = None

