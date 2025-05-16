
# Path: src/roguelike_game/game/map_manager.py
from roguelike_engine.map.controller.map_controller import build_map
from roguelike_engine.map.controller.map_service import _calculate_dungeon_offset
import roguelike_engine.config_map as config_map
import roguelike_engine.config_tiles as config_tiles
from roguelike_engine.map.view.map_view import MapView

class MapManager:
    def __init__(self, map_name: str | None):
        self.result             = build_map(map_mode="global", map_name=map_name)
        
        self.name               = self.result.name
        self.matrix             = self.result.matrix
        self.overlay            = self.result.overlay
        self.tiles              = self.result.tiles
        self.lobby_offset       = self.result.metadata.get("lobby_offset", (0, 0))
        self.rooms              = self.result.metadata.get("rooms", [])        
        
        lob_x, lob_y            = self.lobby_offset
        self.dungeon_offset     = _calculate_dungeon_offset((lob_x, lob_y),config_map.DUNGEON_CONNECT_SIDE)
        self.tiles_in_region    = self.get_tiles_in_region() 

        self.view           = MapView()            # Vista para renderizar el mapa 

    
    def get_tiles_in_region(self) -> list:
        """Devuelve sólo los tiles del lobby y de la dungeon."""
        lob_x, lob_y = self.lobby_offset
        dun_x, dun_y = self.dungeon_offset
        out = []

        for row in self.tiles:
            for tile in row:
                tx = tile.x // config_tiles.TILE_SIZE
                ty = tile.y // config_tiles.TILE_SIZE

                in_lobby = (lob_x <= tx < lob_x + config_map.ZONE_WIDTH
                         and lob_y <= ty < lob_y + config_map.ZONE_HEIGHT)
                in_dungeon = (dun_x <= tx < dun_x + config_map.ZONE_WIDTH
                           and dun_y <= ty < dun_y + config_map.ZONE_HEIGHT)

                if in_lobby or in_dungeon:
                    out.append(tile)

        return out