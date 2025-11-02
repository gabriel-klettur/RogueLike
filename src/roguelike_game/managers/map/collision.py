"""
Módulo de gestión de colisiones por zona.
"""
from pathlib import Path
import json
from roguelike_engine.config.map_config import global_map_settings

import logging
logger = logging.getLogger(__name__)

class CollisionManager:
    """
    Carga y gestiona colisiones de zonas.
    """
    def __init__(self):
        self.collision_layers: dict[str, list[list[str]]] = {}
        self.manager = None

    def load(self, manager) -> dict[str, list[list[str]]]:
        """
        Carga colisiones desde JSON o de la matriz global.
        """
        self.manager = manager
        collisions_dir = global_map_settings.collisions_dir
        collisions_dir.mkdir(parents=True, exist_ok=True)

        for zone, tiles in manager.tiles_by_zone.items():
            file_path = collisions_dir / f"{zone}.json"
            data = None
            if zone != "dungeon" and file_path.exists():
                try:
                    data = json.loads(file_path.read_text(encoding='utf-8'))
                except Exception as e:
                    logger.warning(f"No se pudo leer colisiones para zona {zone}: {e}")
            if data is None:
                offx, offy = global_map_settings.zone_offsets.get(zone, (0,0))
                width = global_map_settings.zone_width
                height = global_map_settings.zone_height
                data = [
                    list(manager.matrix[offy + y][offx:offx + width])
                    for y in range(height)
                ]
                # No auto-escribir archivos: persistir solo al guardar explícitamente

            self.collision_layers[zone] = data
            # Aplicar a tiles
            offx, offy = global_map_settings.zone_offsets.get(zone, (0,0))
            for y, row in enumerate(data):
                for x, code in enumerate(row):
                    gr, gc = offy + y, offx + x
                    try:
                        tile = manager.tiles[gr][gc]
                        tile.solid = (code == '#')
                    except IndexError:
                        continue

        # Reconstruir lista de sólidos
        manager.solid_tiles = [t for r in manager.tiles for t in r if getattr(t, 'solid', False)]
        return self.collision_layers

    def save(self, zone_name: str) -> None:
        """
        Guarda la capa de colisiones de una zona.
        """
        if zone_name == 'dungeon':
            return
        collisions_dir = global_map_settings.collisions_dir
        collisions_dir.mkdir(parents=True, exist_ok=True)
        data = self.collision_layers.get(zone_name)
        if data is None:
            return
        file_path = collisions_dir / f"{zone_name}.json"
        try:
            file_path.write_text(json.dumps(data), encoding='utf-8')
        except Exception as e:
            logger.warning(f"No se pudo guardar colisiones para zona {zone_name}: {e}")

    def is_walkable(self, x: int, y: int) -> bool:
        """
        Indica si la casilla en coordenadas de tile es transitable.
        """
        try:
            tile = self.manager.tiles[y][x]
            return not getattr(tile, 'solid', False)
        except Exception:
            return False
