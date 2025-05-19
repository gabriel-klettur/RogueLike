# Path: src/roguelike_engine/map/model/loader/text_loader_strategy.py
from typing   import List, Optional, Tuple
from .interfaces            import MapLoader
from .text_loader           import parse_map_text
from roguelike_engine.tile.loader       import load_tiles_from_text
from roguelike_engine.map.model.overlay.overlay_manager import load_overlay
from roguelike_engine.map.model.overlay.overlay_manager import save_overlay
from roguelike_engine.tile.model.tile       import Tile

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

        # 2.1) Adaptar overlay si cambia de dimensiones
        height = len(map_data)
        width = len(map_data[0]) if height > 0 else 0
        if overlay is not None:
            ov_h = len(overlay)
            ov_w = len(overlay[0]) if ov_h > 0 else 0
            if ov_h != height or ov_w != width:
                print(f"[TextMapLoader] Adaptando overlay '{map_name}': {ov_w}x{ov_h} -> {width}x{height}")
                # Ajustar filas existentes
                new_overlay: List[List[str]] = []
                for row in overlay:
                    # Expandir o recortar cada fila
                    if len(row) < width:
                        new_row = row + [""] * (width - len(row))
                    else:
                        new_row = row[:width]
                    new_overlay.append(new_row)
                # Añadir filas vacías si faltan
                for _ in range(height - len(new_overlay)):
                    new_overlay.append([""] * width)
                overlay = new_overlay
                # Guardar overlay adaptado
                save_overlay(map_name, overlay)

        # 3) Crear tiles
        tiles = load_tiles_from_text(map_data, overlay)
        return matrix, tiles, overlay