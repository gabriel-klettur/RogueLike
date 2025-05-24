from dataclasses import dataclass

@dataclass
class Scale:
    """
    Componente que define el factor de escala para el sprite de una entidad.
    scale: float (1.0 = tamaño original).
    """
    scale: float = 1.0
