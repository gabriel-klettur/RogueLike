
# Path: src/roguelike_engine/map/model/generator/interfaces.py
from abc import ABC, abstractmethod
from typing import List, Tuple, Optional, Dict

class MapGenerator(ABC):
    """
    Interfaz para generadores de mapa.
    """
    @abstractmethod
    def generate(
        self,
        width: int,
        height: int,
        **kwargs
    ) -> Tuple[List[List[str]], Optional[Dict]]:
        """
        Genera y devuelve una tupla (map_matrix, metadata).
        metadata puede incluir posiciones de habitaciones, conectores, etc.
        """
        ...