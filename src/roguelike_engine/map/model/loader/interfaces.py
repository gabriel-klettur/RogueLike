# Path: src/roguelike_engine/map/model/loader/interfaces.py
from abc import ABC, abstractmethod
from typing import List, Optional, Tuple, Dict
from roguelike_engine.tile.model.tile import Tile
from roguelike_engine.map.model.layer import Layer

class MapLoader(ABC):
    @abstractmethod
    def load(
        self,
        map_data: List[str],
        map_name: str
    ) -> Tuple[List[List[str]], Dict[Layer, List[List[Tile]]], Dict[Layer, List[List[str]]]]:
        """
        Lee la representación textual de un mapa y su overlay, devuelve
        (matrix, tiles, overlay_map).
        """
        ...