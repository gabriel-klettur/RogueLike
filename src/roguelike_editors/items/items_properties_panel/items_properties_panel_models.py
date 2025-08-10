from dataclasses import dataclass, field
from typing import Optional, List, Tuple
import pygame


@dataclass
class ItemsPropertiesPanelModel:
    """Estado del panel de propiedades (solo UI de propiedades)."""
    # Propiedad enfocada y en edición
    focused_property: Optional[str] = None
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
