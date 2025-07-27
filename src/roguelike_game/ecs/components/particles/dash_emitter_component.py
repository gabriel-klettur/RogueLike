from dataclasses import dataclass
from typing import Tuple

@dataclass
class DashEmitterComponent:
    """
    Componente ECS para describir parámetros de emisión de partículas de dash.
    """
    count: int  # número de partículas por tick de dash
    lifespan: int  # duración en frames de cada partícula
    size_range: Tuple[int, int]  # rango (min, max) de tamaño
    color_choices: Tuple[Tuple[int, int, int], ...]  # posibles colores para las partículas
    speed_range: Tuple[float, float]  # rango de velocidad para partículas
