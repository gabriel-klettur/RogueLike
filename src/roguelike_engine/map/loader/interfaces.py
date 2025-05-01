from abc import ABC, abstractmethod
from typing import List, Optional, Tuple
from roguelike_engine.tiles.model import Tile

class MapLoader(ABC):
    @abstractmethod
    def load(
        self,
        map_data: List[str],
        map_name: str
    ) -> Tuple[List[List[str]], List[List[Tile]], Optional[List[List[str]]]]:
        """
        Lee la representación textual de un mapa y su overlay, devuelve
        (matrix, tiles, overlay_map).
        """
        ...
