"""
Módulo de generación incremental de zonas y regeneración de tiles.
"""
from pathlib import Path
import json
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.model.generator.factory import get_generator
from roguelike_engine.map.model.loader.text_loader_strategy import TextMapLoader
from roguelike_engine.map.model.layer import Layer
from roguelike_game.factories.player.config import RENDERED_SPRITE_SIZE
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile, calculate_dungeon_offset
from roguelike_game.ecs.utils import map_utils, spawn_utils

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
        for y in range(old_h):
            for x in range(old_w):
                grid[y + dy][x + dx] = old_matrix[y][x]
        off_x, off_y = new_offsets[zone_key]
        for ry, row in enumerate(zone_matrix):
            for rx, ch in enumerate(row):
                grid[off_y + ry][off_x + rx] = ch

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
        manager.matrix = [''.join(r) for r in grid]
        loader = TextMapLoader()
        _, new_tiles_by_layer, new_raw_layers = loader.load(manager.matrix, manager.name)
        manager.layers = new_raw_layers
        manager.tiles_by_layer = new_tiles_by_layer
        manager.overlay = new_raw_layers.get(Layer.Ground)
        manager.tiles = manager.overlay  # rebuild full tiles externally if needed
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
