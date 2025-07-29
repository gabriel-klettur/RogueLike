from dataclasses import dataclass, field
from typing import Dict, Any, Optional, List, Tuple
import pygame

@dataclass
class EntityPropertiesPanelModel:
    """Estado del panel de propiedades para la entidad seleccionada."""
    player_stats: Dict[str, Any]
    player_assets: Dict[str, Any]
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
    # Pestañas del panel: 'properties' y 'assets'
    tabs: List[str] = field(default_factory=lambda: ['properties', 'assets'])
    active_tab: str = 'properties'
    tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
    # Subtabs (manejado por EntitiesStateTabsController)
    
    
    
    # Celdas de grid de assets (rect y key)
    asset_cell_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)
    # Asset key hovered y seleccionado en grid
    hovered_asset_cell: Optional[str] = None
    selected_asset_cell: Optional[str] = None
