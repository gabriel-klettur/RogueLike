from dataclasses import dataclass, field
from typing import Optional, List, Tuple, Dict, Any
import pygame


@dataclass
class ItemsPropertiesPanelModel:
    """Estado del panel de propiedades (solo UI de propiedades)."""
    # Propiedad enfocada y en edición
    focused_property: Optional[str] = None
    # Propiedad bajo el cursor (para hover)
    hovered_property: Optional[str] = None
    editing_property: Optional[str] = None
    editing_text: str = ""
    editing_cursor: int = 0

    # Rectángulo del panel (para detectar clics externos)
    panel_rect: Optional[pygame.Rect] = None
    # Entradas de propiedades: lista de (rect, key)
    property_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)

    # Scroll y métricas de contenido (para panel de tamaño fijo)
    scroll_y: int = 0
    content_height: int = 0
    content_view_rect: Optional[pygame.Rect] = None

    # Pestañas principales: 'properties' y 'assets'
    type_tabs: List[str] = field(default_factory=lambda: ["properties", "assets"])
    active_type_tab: str = "properties"
    type_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)

    # Única celda para asset del ítem (icono)
    asset_cell_rect: Optional[pygame.Rect] = None

    # Modo "añadir ítem al sistema" (controlado desde la sub-toolbar Add/Remove)
    # La vista actual no dibuja un selector dedicado, pero este flag permite
    # coordinar el layout con el editor y mostrar/ocultar controles si fuese necesario.
    show_add_system_selector: bool = False

    # Claves (esquema) unificadas extraídas de data/items/items.json para poder crear nuevos ítems
    schema_keys: List[str] = field(default_factory=list)
    # Borrador de nuevo ítem cuando no hay ítem activo en modo add-on-system
    new_item_draft: Dict[str, Any] = field(default_factory=dict)
