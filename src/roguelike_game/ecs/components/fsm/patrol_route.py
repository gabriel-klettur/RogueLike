# Path: src/roguelike_game/ecs/components/fsm/patrol_route.py
from dataclasses import dataclass
from typing import List, Tuple

@dataclass
class PatrolRoute:
    """
    Componente que almacena la lista de waypoints para la patrulla.
    Cada waypoint es una tupla (x, y) en coordenadas del mundo.
    """
    points: List[Tuple[float, float]]