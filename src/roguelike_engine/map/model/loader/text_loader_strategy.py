from typing import List, Optional, Tuple
from .interfaces import MapLoader
from .text_loader import parse_map_text
from roguelike_engine.tile.loader import load_tiles_from_text
from roguelike_engine.map.model.overlay.overlay_manager import load_overlay, save_overlay
from roguelike_engine.tile.model.tile import Tile
from roguelike_engine.config.map_config import global_map_settings

# Importar el generador de overlay_map (códigos → assets)
from scripts.generate_overlay_map import main as generate_overlay_map

class TextMapLoader(MapLoader):
    def load(
        self,
        map_data: List[str],
        map_name: str
    ) -> Tuple[List[List[str]], List[List[Tile]], Optional[List[List[str]]]]:
        # 0) (Re)generar el mapping de overlay codes → asset names
        generate_overlay_map()

        # 1) Parsear la representación textual en matriz de caracteres
        matrix = parse_map_text(map_data)

        # 2) Intentar cargar overlays por zona en lugar del global
        overlays: dict[str, tuple[int, int, List[List[str]]]] = {}
        for zone, (ox, oy) in global_map_settings.zone_offsets.items():
            ov = load_overlay(zone)
            if ov is not None:
                overlays[zone] = (ox, oy, ov)

        if overlays:
            # Construir overlay combinado para el mapa global
            height = len(map_data)
            width = len(map_data[0]) if height > 0 else 0
            combined: List[List[str]] = [["" for _ in range(width)] for _ in range(height)]
            for zone, (ox, oy, ov) in overlays.items():
                for y, row in enumerate(ov):
                    for x, code in enumerate(row):
                        combined[oy + y][ox + x] = code
            overlay = combined
        else:
            # Fallback al overlay global (anterior)
            overlay = load_overlay(map_name)

        # 3) Adaptar dimensiones si han cambiado
        height = len(map_data)
        width = len(map_data[0]) if height > 0 else 0
        if overlay is not None:
            ov_h = len(overlay)
            ov_w = len(overlay[0]) if ov_h > 0 else 0
            if ov_h != height or ov_w != width:
                print(f"[TextMapLoader] Adaptando overlay '{map_name}': {ov_w}x{ov_h} → {width}x{height}")
                # Ajustar filas existentes
                new_overlay: List[List[str]] = []
                for row in overlay:
                    if len(row) < width:
                        new_overlay.append(row + [""] * (width - len(row)))
                    else:
                        new_overlay.append(row[:width])
                # Añadir filas faltantes
                for _ in range(height - len(new_overlay)):
                    new_overlay.append([""] * width)
                overlay = new_overlay
                save_overlay(map_name, overlay)

        # 4) Generar tiles usando el overlay combinado
        tiles = load_tiles_from_text(map_data, overlay)
        return matrix, tiles, overlay
