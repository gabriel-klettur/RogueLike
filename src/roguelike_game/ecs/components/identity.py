from dataclasses import dataclass
from enum import Enum

class Faction(Enum):
    GOOD = 'good'
    NEUTRAL = 'neutral'
    EVIL = 'evil'

@dataclass
class Identity:
    """
    Componente que define nombre, título y facción de la entidad.
    """
    name: str
    title: str
    faction: Faction
