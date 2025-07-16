from dataclasses import dataclass, field
from typing import List, Optional

@dataclass
class InventoryPanelModel:
    """
    Modelo para la vista de listado de entidades (tabs + lista scroll).
    """
    categories: List[str] = field(default_factory=lambda: ['player', 'monsters', 'map'])
    current_category: str = 'player'
    selected_eid: Optional[str] = None
