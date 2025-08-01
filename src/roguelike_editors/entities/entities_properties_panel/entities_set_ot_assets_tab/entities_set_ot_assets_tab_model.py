from dataclasses import dataclass, field
from typing import List, Dict
import pygame

@dataclass
class EntitiesSetOtAssetsTabModel:
    """Model for the 'Asset Set' and 'No-Set' subtabs."""
    sub_tabs: List[str] = field(default_factory=lambda: ['asset set', 'no-set'])
    active_sub_tab: str = 'asset set'
    sub_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
