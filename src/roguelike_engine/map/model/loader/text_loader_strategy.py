from typing import List, Optional, Tuple, Dict
from .interfaces import MapLoader
from .text_loader import parse_map_text
from roguelike_engine.tile.loader import load_tiles_from_text
from roguelike_engine.tile.model.tile import Tile
from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.config.map_config import global_map_settings

# Importar el generador de overlay_map (códigos → assets)
from scripts.generate_overlay_map import main as generate_overlay_map

class TextMapLoader(MapLoader):
    def load(
        self,
        map_data: List[str],
        map_name: str
    ) -> Tuple[List[List[str]], Dict[Layer, List[List[Tile]]], Dict[Layer, List[List[str]]]]:
        # 0) (Re)generar el mapping de overlay codes → asset names
        generate_overlay_map()

        # 1) Parsear la representación textual en matriz de caracteres
        matrix = parse_map_text(map_data)

        # 2) Cargar todas las capas (nuevo o legacy)
        raw_layers = load_layers(map_name)
        height = len(map_data)
        width = len(map_data[0]) if height > 0 else 0
        # Si no hay capas, inicializar Ground vacío
        if not raw_layers:
            raw_layers = {Layer.Ground: [["" for _ in range(width)] for _ in range(height)]}
        # 3) Adaptar dimensiones de cada capa
        adapted = False
        for layer, grid in raw_layers.items():
            h = len(grid)
            w = len(grid[0]) if h > 0 else 0
            if h != height or w != width:
                adapted = True
                new_grid = []
                # pad/truncate rows
                for row in grid:
                    if len(row) < width:
                        new_grid.append(row + [""] * (width - len(row)))
                    else:
                        new_grid.append(row[:width])
                # add missing rows
                for _ in range(height - len(new_grid)):
                    new_grid.append([""] * width)
                raw_layers[layer] = new_grid
        if adapted:
            print(f"[TextMapLoader] Adaptando capas para '{map_name}' a {width}x{height}")
            save_layers(map_name, raw_layers)
        # Merge overlays por zona en cada capa existente o nueva
        for zone_name, (off_x, off_y) in global_map_settings.zone_offsets.items():
            zone_layers = load_layers(zone_name)
            if not zone_layers:
                continue
            for layer, zgrid in zone_layers.items():
                # asegurar capa en raw_layers
                base = raw_layers.setdefault(layer, [["" for _ in range(width)] for _ in range(height)])
                # superponer códigos
                for y0, zrow in enumerate(zgrid):
                    for x0, code in enumerate(zrow):
                        ty = off_y + y0
                        tx = off_x + x0
                        if 0 <= ty < height and 0 <= tx < width and code:
                            base[ty][tx] = code
        # 4) Generar tiles por capa
        tiles_by_layer: Dict[Layer, List[List[Tile]]] = {}
        for layer, grid in raw_layers.items():
            tiles_by_layer[layer] = load_tiles_from_text(map_data, grid)
        return matrix, tiles_by_layer, raw_layers
