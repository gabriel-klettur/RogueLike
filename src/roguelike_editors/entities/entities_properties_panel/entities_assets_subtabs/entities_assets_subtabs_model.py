from dataclasses import dataclass, field
from typing import Dict, List
import pygame
from roguelike_editors.entities.entities_properties_panel.services.assets_constants import (
    SUBTAB_SET,
    SUBTAB_NO_SET,
)


@dataclass
class EntitiesAssetsSubTabsModel:
    """Model for the 'Asset Set' and 'No-Set' subtabs."""

    sub_tabs: List[str] = field(default_factory=lambda: [SUBTAB_SET, SUBTAB_NO_SET])
    active_sub_tab: str = SUBTAB_SET
    sub_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
