from dataclasses import dataclass
from typing import List, Tuple, Optional

@dataclass
class PatrolRoute:
    """
    Componente que almacena la lista de waypoints para la patrulla.
    Cada waypoint es una tupla (x, y) en coordenadas del mundo.
    Opcionalmente puede incluir tiempos de espera (dwell) por waypoint
    para pausas entre desplazamientos.
    """
    points: List[Tuple[float, float]]
    dwell_times: Optional[List[float]] = None