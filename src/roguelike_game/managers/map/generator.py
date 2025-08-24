"""
Módulo de generación incremental de zonas y regeneración de tiles.
"""
from pathlib import Path
import json
import logging
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.model.generator.factory import get_generator
from roguelike_engine.map.model.loader.text_loader_strategy import TextMapLoader
from roguelike_engine.map.model.layer import Layer
from roguelike_game.factories.player.config import RENDERED_SPRITE_SIZE
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile, calculate_dungeon_offset
from roguelike_game.ecs.utils import map_utils, spawn_utils

logger = logging.getLogger(__name__)

class MapGenerator:
    """
    Gestiona expansión incremental de zonas.
    """
    def expand_zone(self, manager, side: str, zone_key: str, parent_key: str) -> None:
        # Ajustes dinámicos
        global_map_settings.use_zones_json = False
        old_offsets = global_map_settings.zone_offsets.copy()
        global_map_settings.__dict__.pop('zone_offsets', None)
        new_offsets = global_map_settings.zone_offsets

        # Generar nueva zona
        gen = get_generator()
        raw_map, metadata_zone = gen.generate(
            width=global_map_settings.zone_width,
            height=global_map_settings.zone_height,
            return_rooms=True
        )
        zone_matrix = [''.join(row) for row in raw_map]

        # Construir matriz global
        old_matrix = manager.matrix
        old_h = len(old_matrix)
        old_w = len(old_matrix[0]) if old_h else 0
        new_h = global_map_settings.global_height
        new_w = global_map_settings.global_width
        grid = [['#' for _ in range(new_w)] for _ in range(new_h)]
        dx = new_offsets[parent_key][0] - old_offsets[parent_key][0]
        dy = new_offsets[parent_key][1] - old_offsets[parent_key][1]
        # Preservar offsets existentes (p. ej. 'Forest') aplicando el mismo desplazamiento global
        merged_offsets = new_offsets.copy()
        for name, (ox, oy) in old_offsets.items():
            if name not in merged_offsets:
                merged_offsets[name] = (ox + dx, oy + dy)
        # Asegurar sentinelas
        merged_offsets.setdefault('no zone', (0, 0))
        merged_offsets.setdefault('no-zone', (0, 0))
        # Inyectar offsets fusionados en el cache del cached_property
        global_map_settings.__dict__['zone_offsets'] = merged_offsets
        truncated = False
        for y in range(old_h):
            ny = y + dy
            if ny < 0 or ny >= new_h:
                truncated = True
                continue
            for x in range(old_w):
                nx = x + dx
                if 0 <= nx < new_w:
                    grid[ny][nx] = old_matrix[y][x]
                else:
                    truncated = True
        off_x, off_y = new_offsets[zone_key]
        for ry, row in enumerate(zone_matrix):
            ny = off_y + ry
            if ny < 0 or ny >= new_h:
                truncated = True
                continue
            for rx, ch in enumerate(row):
                nx = off_x + rx
                if 0 <= nx < new_w:
                    grid[ny][nx] = ch
                else:
                    truncated = True

        if truncated:
            logger.warning(
                "Map expand pasted outside bounds (clamped). new_w=%d new_h=%d dx=%d dy=%d off_x=%d off_y=%d old_w=%d old_h=%d",
                new_w, new_h, dx, dy, off_x, off_y, old_w, old_h
            )

        # Conectar zonas
        from roguelike_engine.map.model.generator.dungeon import DungeonGenerator
        import random
        parent_rooms = manager.zone_rooms.get(parent_key, [])
        new_rooms = metadata_zone.get('rooms', [])
        if parent_rooms and new_rooms:
            parent_centers = [((r[0]+r[2])//2 + new_offsets[parent_key][0], (r[1]+r[3])//2 + new_offsets[parent_key][1]) for r in parent_rooms]
            new_centers = [((r[0]+r[2])//2 + new_offsets[zone_key][0], (r[1]+r[3])//2 + new_offsets[zone_key][1]) for r in new_rooms]
            min_pair = None
            min_dist = float('inf')
            for pc in parent_centers:
                for nc in new_centers:
                    d = abs(pc[0]-nc[0]) + abs(pc[1]-nc[1])
                    if d < min_dist:
                        min_dist = d
                        min_pair = (pc, nc)
            if min_pair:
                (px, py), (nx, ny) = min_pair
                if random.random() < 0.5:
                    DungeonGenerator._horiz_tunnel(grid, px, nx, py)
                    DungeonGenerator._vert_tunnel(grid, py, ny, nx)
                else:
                    DungeonGenerator._vert_tunnel(grid, py, ny, px)
                    DungeonGenerator._horiz_tunnel(grid, px, nx, ny)

        # Actualizar manager con nueva zona
        # Guardar rooms por zona para sistemas que lo consumen (coordenadas relativas)
        manager.zone_rooms[zone_key] = new_rooms
        manager.matrix = [''.join(r) for r in grid]
        loader = TextMapLoader()
        _, new_tiles_by_layer, new_raw_layers = loader.load(manager.matrix, manager.name)
        manager.layers = new_raw_layers
        manager.tiles_by_layer = new_tiles_by_layer
        manager.overlay = new_raw_layers.get(Layer.Ground)
        # Tiles deben ser objetos Tile (no strings). Usar la capa Ground de tiles_by_layer.
        manager.tiles = new_tiles_by_layer.get(Layer.Ground, [])
        manager.solid_tiles = [t for row in manager.tiles for t in row if getattr(t, 'solid', False)]

        # Reetiquetar zonas
        manager.tiles_by_zone.clear()
        for row in manager.tiles:
            for tile in row:
                tx = tile.x // TILE_SIZE
                ty = tile.y // TILE_SIZE
                tile.zone = get_zone_for_tile(tx, ty)
                manager.tiles_by_zone.setdefault(tile.zone, []).append(tile)

        # Refrescar vista
        manager.tiles_in_region = map_utils.flatten_tiles(manager.tiles)
        manager.renderer.view.invalidate_cache()
