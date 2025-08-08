from dataclasses import dataclass, field
from typing import List, Dict
import pygame
from roguelike_editors.entities.entities_properties_panel.services.state_constants import (
    STATE_TABS_DEFAULT,
)

@dataclass
class EntitiesStateTabsModel:
    """Modelo para las pestañas de estado de la entidad."""
    # Lista de pestañas de estado visibles en UI.
    # Tomada de servicios/constantes para mantener consistencia.
    state_tabs: List[str] = field(default_factory=lambda: STATE_TABS_DEFAULT.copy())
    active_state_tab: str = 'idle'
    state_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
