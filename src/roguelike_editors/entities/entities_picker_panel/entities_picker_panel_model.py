from dataclasses import dataclass
from typing import Dict, Any, Optional
import pygame

@dataclass
class EntityPickerPanelModel:
    """Estado del editor de entidades: jugador y monstruos."""
    player_stats: Dict[str, Any]
    monsters: Dict[str, Any]
    assets: Dict[str, pygame.Surface]

    visible: bool = False
    scroll_index: int = 0
    hovered_id: Optional[str] = None
    selected_id: Optional[str] = None

