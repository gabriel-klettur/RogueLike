from dataclasses import dataclass, field
from typing import Dict, Any, Optional, List, Tuple
import pygame

# Type aliases for clarity
RectEntry = Tuple[pygame.Rect, str]

@dataclass
class EntityPropertiesPanelModel:
    """Estado del panel de propiedades para la entidad seleccionada."""
    player_stats: Dict[str, Any]
    player_assets: Dict[str, Any]
    monsters: Dict[str, Any]
    # Data selection
    selected_id: Optional[str] = None
    hovered_entity_id: Optional[str] = None
    
    # UI geometry and interactive entries
    panel_rect: Optional[pygame.Rect] = None
    property_entries: List[RectEntry] = field(default_factory=list)
    focused_property: Optional[str] = None
    editing_property: Optional[str] = None
    editing_text: str = ""
    editing_cursor: int = 0
    hovered_property: Optional[str] = None
    # Scroll support for properties list
    scroll_offset: int = 0
    max_scroll: int = 0
    total_lines_height: int = 0
    available_height: int = 0
    # Scroll support for assets tab (separate state to keep uniform UX)
    assets_scroll_offset: int = 0
    assets_max_scroll: int = 0
    assets_total_height: int = 0
    assets_available_height: int = 0
    
    
    
    # Subtabs (manejado por EntitiesStateTabsController)
    
    
    # Celdas de grid de assets (rect y key)
    asset_cell_entries: List[RectEntry] = field(default_factory=list)
    # Asset key hovered y seleccionado en grid
    hovered_asset_cell: Optional[str] = None
    selected_asset_cell: Optional[str] = None

    # Selector de tipo de entidad (visible cuando se usa 'add_entities_on_system')
    show_add_system_selector: bool = False
    add_system_entity_type: str = "Hostile"
    entity_type_rect: Optional[pygame.Rect] = None
    # Botón de confirmación (visible solo en modo 'Add Entities on System')
    confirm_button_rect: Optional[pygame.Rect] = None
    
    # Layout override flags for ADD_ENTITIES_ON_SYSTEM mode
    # When active, the properties panel should expand into the area usually used by the picker.
    expand_into_picker_space: bool = False
    # Left X coordinate to anchor the panel when expanding (typically the picker's X)
    panel_left_x_override: Optional[int] = None
    # Previous draggable position to restore when exiting the mode
    saved_drag_pos: Optional[Tuple[int, int]] = None
