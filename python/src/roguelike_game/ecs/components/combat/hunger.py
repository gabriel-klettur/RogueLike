from dataclasses import dataclass

@dataclass
class Hunger:
    """
    Componente que representa el nivel de hambre o saciedad de una entidad.
    """
    current_hunger: int
    max_hunger: int
