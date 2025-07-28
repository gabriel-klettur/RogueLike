from dataclasses import dataclass, field
from typing import Dict, Any, Optional, List, Tuple
import pygame

@dataclass
class EntityPropertiesPanelModel:
    """Estado del panel de propiedades para la entidad seleccionada."""
    player_stats: Dict[str, Any]
    monsters: Dict[str, Any]
    selected_id: Optional[str] = None
    hovered_entity_id: Optional[str] = None
    panel_rect: Optional[pygame.Rect] = None
    property_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)
    focused_property: Optional[str] = None
    editing_property: Optional[str] = None
    editing_text: str = ""
    editing_cursor: int = 0
    hovered_property: Optional[str] = None
