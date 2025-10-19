from dataclasses import dataclass
from typing import Dict, Any, Optional
import pygame
from dataclasses import field
from typing import List, Tuple

@dataclass
class EntityPickerPanelModel:
    """Estado del editor de entidades: jugadores, hostiles y neutrales.

    Nota: 'monsters' fue un alias hacia 'hostiles'. Se mantiene por compatibilidad,
    pero para neutrales se usa el campo dedicado 'neutrals'.
    """
    player_stats: Dict[str, Any]
    hostiles: Dict[str, Any]
    neutrals: Dict[str, Any]
    assets: Dict[str, pygame.Surface]
    # Área del panel para interacción y arrastre
    panel_rect: Optional[pygame.Rect] = None

    # Precomputed clickable rects for grid items
    item_entries: List[Tuple[pygame.Rect, str]] = field(default_factory=list)

    visible: bool = False
    scroll_index: int = 0
    hovered_id: Optional[str] = None
    selected_id: Optional[str] = None
    # Pestaña activa: 'Players', 'Hostile', 'Neutral', 'Aliades', 'Specials'
    active_tab: str = "Players"
    # Rectángulos de las pestañas para detectar clicks
    tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
    # Blink del picker en modo spawn
    blink: bool = False
    # Parpadeo de la selección
    selection_blink: bool = False

    # ----------------------------
    # Compatibilidad temporal
    # ----------------------------
    @property
    def monsters(self) -> Dict[str, Any]:
        """Alias temporal para compatibilidad hacia atrás."""
        return self.hostiles

    @monsters.setter
    def monsters(self, value: Dict[str, Any]) -> None:
        self.hostiles = value
