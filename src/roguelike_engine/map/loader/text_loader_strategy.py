# Path: src/roguelike_engine/map/loader/text_loader_strategy.py

from typing import List, Optional, Tuple
from .interfaces import MapLoader
from .text_loader import parse_map_text
from roguelike_engine.tiles.loader import load_tiles_from_text
from roguelike_engine.map.overlay.overlay_manager import load_overlay
from roguelike_engine.tiles.model import Tile

class TextMapLoader(MapLoader):
    def load(
        self,
        map_data: List[str],
        map_name: str
    ) -> Tuple[List[List[str]], List[List[Tile]], Optional[List[List[str]]]]:
        matrix = parse_map_text(map_data)
        overlay = load_overlay(map_name)
        tiles = load_tiles_from_text(map_data, overlay)
        return matrix, tiles, overlay