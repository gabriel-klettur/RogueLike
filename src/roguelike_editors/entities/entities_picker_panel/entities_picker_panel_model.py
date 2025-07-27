from dataclasses import dataclass
from typing import Dict, Any, Optional
import pygame
from dataclasses import field
from typing import List, Tuple

@dataclass
class EntityPickerPanelModel:
    """Estado del editor de entidades: jugador y monstruos."""
    player_stats: Dict[str, Any]
    monsters: Dict[str, Any]
    assets: Dict[str, pygame.Surface]
    # Área del panel para interacción y arrastre
    panel_rect: Optional[pygame.Rect] = None

    # Precomputed clickable rects for grid items
    item_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)

    visible: bool = False
    scroll_index: int = 0
    hovered_id: Optional[str] = None
    selected_id: Optional[str] = None
    # Blink del picker en modo spawn
    blink: bool = False
