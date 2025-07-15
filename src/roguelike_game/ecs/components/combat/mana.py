from dataclasses import dataclass

@dataclass
class Mana:
    """
    Componente que representa la energía mágica (maná) de una entidad.
    """
    current_mana: int
    max_mana: int
