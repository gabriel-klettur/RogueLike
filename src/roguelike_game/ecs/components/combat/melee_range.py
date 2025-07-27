from dataclasses import dataclass

@dataclass
class MeleeRange:
    """
    Componente que almacena el rango de ataque cuerpo a cuerpo en tiles.
    """
    range: int