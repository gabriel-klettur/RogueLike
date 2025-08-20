from dataclasses import dataclass
from typing import List, Tuple, Optional

@dataclass
class PatrolRoute:
    """
    Componente que almacena la lista de waypoints para la patrulla.
    Cada waypoint es una tupla (x, y) en coordenadas del mundo.
    Opcionalmente puede incluir tiempos de espera (dwell) por waypoint
    para pausas naturales entre desplazamientos.
    """
    points: List[Tuple[float, float]]
    dwell_times: Optional[List[float]] = None
    # Opcional: metadatos para patrones con área dinámica (p.ej. 'natural')
    pattern_id: Optional[str] = None
    area_center: Optional[Tuple[float, float]] = None
    area_radius: Optional[float] = None
    min_step: Optional[float] = None