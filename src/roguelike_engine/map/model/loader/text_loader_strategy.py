# Path: src/roguelike_engine/map/model/loader/text_loader_strategy.py
from typing   import List, Optional, Tuple
from .interfaces            import MapLoader
from .text_loader           import parse_map_text
from roguelike_engine.tiles.loader       import load_tiles_from_text
from roguelike_engine.map.model.overlay.overlay_manager import load_overlay
from roguelike_engine.tiles.model       import Tile

# Importar el generador de overlay
from scripts.generate_overlay_map import main as generate_overlay_map

class TextMapLoader(MapLoader):
    def load(
        self,
        map_data: List[str],
        map_name: str
    ) -> Tuple[List[List[str]], List[List[Tile]], Optional[List[List[str]]]]:

        # 0) Regenerar automáticamente el overlay map
        generate_overlay_map()

        # 1) Parsear el mapa
        matrix = parse_map_text(map_data)
        # 2) Cargar overlay ya generado
        overlay = load_overlay(map_name)
        # 3) Crear tiles
        tiles = load_tiles_from_text(map_data, overlay)
        return matrix, tiles, overlay