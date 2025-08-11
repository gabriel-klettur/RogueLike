from dataclasses import dataclass, field
from typing import Optional, List, Tuple, Dict, Any
import pygame


@dataclass
class SpellsPropertiesPanelModel:
    """UI state for the Spells Properties panel.

    Mirrors ItemsPropertiesPanelModel but tailored for spells.
    """
    # Focus and editing state
    focused_property: Optional[str] = None
    hovered_property: Optional[str] = None
    editing_property: Optional[str] = None
    editing_text: str = ""
    editing_cursor: int = 0

    # Panel and content metrics
    panel_rect: Optional[pygame.Rect] = None
    property_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)
    scroll_y: int = 0
    content_height: int = 0
    content_view_rect: Optional[pygame.Rect] = None

    # Tabs
    type_tabs: List[str] = field(default_factory=lambda: ["properties", "assets"])
    active_type_tab: str = "properties"
    type_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)

    # Asset cell (for 'sprite')
    asset_cell_rect: Optional[pygame.Rect] = None

    # Add-on-system (reserved for future parity with Items)
    show_add_system_selector: bool = False
    schema_keys: List[str] = field(default_factory=list)
    new_spell_draft: Dict[str, Any] = field(default_factory=dict)
