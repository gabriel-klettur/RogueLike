from dataclasses import dataclass, field
from typing import List, Dict
import pygame

@dataclass
class EntitiesStateTabsModel:
    """Modelo para las pestañas de estado de la entidad."""
    state_tabs: List[str] = field(default_factory=lambda: ['idle', 'chase', 'attack', 'death', 'damage', 'casting', 'add state'])
    active_state_tab: str = 'idle'
    state_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
