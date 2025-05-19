# Path: src/roguelike_game/game/map_manager.py
from roguelike_engine.map.controller.map_controller import build_map
from roguelike_engine.map.utils import calculate_dungeon_offset
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.map.view.chunked_map_view import ChunkedMapView

from roguelike_engine.config.config_tiles import TILE_SIZE

class MapManager:
    def __init__(self, map_name: str | None):
        # Construir mapa usando MapService
        self.result = build_map(map_name)

        # Propiedades básicas
        self.name = self.result.name
        self.matrix = self.result.matrix
        self.overlay = self.result.overlay
        self.tiles = self.result.tiles
        # Offset del lobby, extraído de metadata
        self.lobby_offset = self.result.metadata.get("lobby_offset", (0, 0))
        # Lista de habitaciones de la dungeon
        self.rooms = self.result.metadata.get("rooms", [])

        # Calcular offset de la dungeon en tiles
        lob_x, lob_y = self.lobby_offset
        self.dungeon_offset = calculate_dungeon_offset((lob_x, lob_y))
        # Todas las tiles en lista plana
        self.tiles_in_region = self.all_tiles

        #!-------------------------------------------------------------------------------------        
        # 1) Para cada tile, calcular su zona
        for row in self.tiles:
            for tile in row:
                tx = tile.x // TILE_SIZE
                ty = tile.y // TILE_SIZE
                tile.zone = get_zone_for_tile(tx, ty)

        # 2) Construir un dict zonas → lista de tiles (opcional, para acelerar el view)
        self.tiles_by_zone: dict[str, list] = {}
        for row in self.tiles:
            for tile in row:
                self.tiles_by_zone.setdefault(tile.zone, []).append(tile)
        #!-------------------------------------------------------------------------------------                

        # Vista optimizada de chunked map
        self.view = ChunkedMapView()

    @property
    def all_tiles(self):
        """
        Devuelve todas las tiles del mapa en una lista plana.
        """
        return [tile for row in self.tiles for tile in row]
