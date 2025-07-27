from dataclasses import dataclass, field
from typing import Dict, Any, Optional, List, Tuple
import pygame

@dataclass
class EntityEditorModel:
    """Estado del editor de entidades: jugador y monstruos."""
    player_stats: Dict[str, Any]
    monsters: Dict[str, Any]
    assets: Dict[str, pygame.Surface]

    visible: bool = False
    scroll_index: int = 0
    hovered_id: Optional[str] = None
    selected_id: Optional[str] = None
    focused_property: Optional[str] = None
    editing_property: Optional[str] = None
    editing_text: str = ""
    editing_cursor: int = 0
    panel_rect: Optional[pygame.Rect] = None
    property_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)
