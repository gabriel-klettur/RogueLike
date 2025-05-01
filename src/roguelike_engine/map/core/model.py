from dataclasses import dataclass
from typing import List, Optional, Dict
from roguelike_engine.tiles.model import Tile

@dataclass
class MapData:
    """
    Contenedor inmutable con todos los datos resultantes de la construcción de un mapa.
    """
    matrix: List[str]                          # Cada elemento es una fila del mapa (“########”, “#...#”, …)
    tiles: List[List[Tile]]                    # Matriz de objetos Tile correspondientes a cada celda
    overlay: Optional[List[List[str]]]         # Capa de overlay (códigos de 3 chars), o None
    metadata: Dict                             # Datos extra (p. ej. lista de habitaciones)
    name: str                                  # Clave usada para persistir el overlay (lobby_map, combined_map…)
