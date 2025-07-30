from dataclasses import dataclass, field
from typing import List, Dict
import pygame

@dataclass
class EntitiesSetOtAssetsTabModel:
    """Model for the 'Asset Set' and 'Asset by Asset' subtabs."""
    sub_tabs: List[str] = field(default_factory=lambda: ['asset set', 'asset by asset'])
    active_sub_tab: str = 'asset set'
    sub_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
