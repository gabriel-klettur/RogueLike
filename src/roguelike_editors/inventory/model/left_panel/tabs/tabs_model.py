from dataclasses import dataclass, field
from typing import List


@dataclass
class TabsModel:
    """
    Modelo para las tabs del panel izquierdo: maneja categorías y categoría actual.
    """
    categories: List[str] = field(default_factory=lambda: ['player', 'monsters', 'map'])
    current_category: str = 'player'
