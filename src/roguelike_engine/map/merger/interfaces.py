#Path: src/roguelike_engine/map/merger/interfaces.py

from abc import ABC, abstractmethod
from typing import List, Tuple, Sequence, Optional

class MergerStrategy(ABC):
    """
    Interfaz para estrategias de fusión de mapas.
    """
    @abstractmethod
    def merge(
        self,
        handmade_map: Sequence[Sequence[str]],
        generated_map: Sequence[Sequence[str]],
        offset_x: int,
        offset_y: int,
        dungeon_rooms: Optional[List[Tuple[int,int,int,int]]] = None
    ) -> List[List[str]]:
        """
        Fusiona handmade_map dentro de generated_map y devuelve la nueva matriz.
        """
        ...