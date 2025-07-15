from dataclasses import dataclass

@dataclass
class Energy:
    """
    Componente que representa la energía física o resistencia de una entidad.
    """
    current_energy: int
    max_energy: int
