from dataclasses import dataclass
from typing import Tuple

@dataclass
class SlashEmitterComponent:
    """
    Componente ECS para describir parámetros de emisión de partículas de slash.
    Se añade al caster al lanzar un slash y el sistema SlashEmitterSystem genera las partículas.
    """
    radius: float  # radio del arco de emisión
    arc_range: float  # amplitud angular del slash (radianes)
    count: int  # número de partículas a emitir
    lifespan: int  # duración en frames de cada partícula
    size_range: Tuple[int, int]  # rango (min, max) de tamaños
    color: Tuple[int, int, int]  # color base de las partículas
    speed_multiplier: float  # multiplicador de velocidad
    direction: Tuple[float, float]  # dirección central del slash (unit vector)
    offset: float  # offset desde el centro para la hitbox y origen de partículas
