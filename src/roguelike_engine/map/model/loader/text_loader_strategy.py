from typing import List, Optional, Tuple, Dict
from .interfaces import MapLoader
from .text_loader import parse_map_text
from roguelike_engine.tile.utils.loader import load_tiles_from_text
from roguelike_engine.tile.model.tile_model import Tile
from roguelike_engine.map.model.overlay.overlay_manager import load_layers
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.config.map_config import global_map_settings

# Importar el generador de overlay_map (códigos → assets)
try:
    from scripts.generate_overlay_map import main as generate_overlay_map
except ImportError:
    def generate_overlay_map():
        print("[TextMapLoader] Warning: scripts.generate_overlay_map not found, skipping overlay generation")

_overlay_map_generated = False

class TextMapLoader(MapLoader):
    def load(
        self,
        map_data: List[str],
        map_name: str
    ) -> Tuple[List[List[str]], Dict[Layer, List[List[Tile]]], Dict[Layer, List[List[str]]]]:
        print(f"[DEBUG][TextMapLoader] load called for map '{map_name}', size={len(map_data)}x{(len(map_data[0]) if map_data else 0)}")
        # 0) (Re)generar el mapping de overlay codes → asset names (solo una vez)
        global _overlay_map_generated
        if not _overlay_map_generated:
            generate_overlay_map()
            _overlay_map_generated = True

        # 1) Parsear la representación textual en matriz de caracteres
        matrix = parse_map_text(map_data)

        # 2) Cargar todas las capas (nuevo o legacy)
        raw_layers = load_layers(map_name)
        print(f"[DEBUG][TextMapLoader] raw_layers for '{map_name}': {[ (layer.name, len(grid)) for layer,grid in raw_layers.items() ]}")
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
        # Merge overlays por zona en cada capa existente o nueva
        for zone_name, (off_x, off_y) in global_map_settings.zone_offsets.items():
            print(f"[DEBUG][TextMapLoader] merging zone '{zone_name}' overlay")
            zone_layers = load_layers(zone_name)
            print(f"[DEBUG][TextMapLoader] zone_layers for '{zone_name}': {[ (layer.name, len(grid)) for layer,grid in zone_layers.items() ]}")
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
        # 4) Asegurar todas las capas (incluso vacías) antes de generar tiles por capa
        for layer in Layer:
            raw_layers.setdefault(layer, [["" for _ in range(width)] for _ in range(height)])
        # Generar tiles por capa
        tiles_by_layer: Dict[Layer, List[List[Tile]]] = {}
        for layer, grid in raw_layers.items():
            tiles_by_layer[layer] = load_tiles_from_text(map_data, grid)
        return matrix, tiles_by_layer, raw_layers