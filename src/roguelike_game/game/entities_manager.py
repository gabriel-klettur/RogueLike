
# Path: src/roguelike_game/game/entities_manager.py
from typing import Tuple, List

from roguelike_game.entities.load_entities import load_entities
from roguelike_game.entities.load_hostile import load_hostile
from roguelike_game.game.map_manager import MapManager
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.config_map import ZONE_OFFSETS
from roguelike_engine.config_map import DUNGEON_CONNECT_SIDE
from roguelike_engine.map.controller.map_service import calculate_dungeon_offset

class EntitiesManager:
    """
    Carga y mantiene entidades del juego: jugador, obstáculos, edificios y enemigos.
    """
    
    def __init__(self, z_state, game_map: MapManager):
        self.z_state = z_state
        self.map = game_map
        
        self.player = None
        self.obstacles = []
        self.buildings = []
        self.enemies = []        

        self.init_statics()
        self.init_enemies()        

    def init_statics(self):
        """
        Carga jugador, obstáculos y edificios.
        Devuelve (player, obstacles, buildings).
        """        
        self.player, self.obstacles, self.buildings = load_entities(self.z_state)
        self.recalibrate_buildings()
        
        return self.player, self.obstacles, self.buildings    

    def init_enemies(self):
        """
        Carga enemigos en la dungeon.
        """
        # 2️⃣ Calcular offset en tiles de la dungeon
        lob_x, lob_y = self.map.lobby_offset
        dungeon_offset = calculate_dungeon_offset(
            (lob_x, lob_y),
            DUNGEON_CONNECT_SIDE
        )
        # 4️⃣ Spawn procedural de enemigos
        self.enemies = self.spawn_enemies(dungeon_offset)

    def spawn_enemies(self, dungeon_offset: Tuple[int, int]) -> List:
        """
        Genera y devuelve la lista de enemigos usando metadata del mapa.
        """
        # Coordenada inicial del jugador en tiles
        px = int(self.player.x) // TILE_SIZE
        py = int(self.player.y) // TILE_SIZE
        player_tile = (px, py)

        rooms = self.map.rooms
        self.enemies = load_hostile(
            rooms,
            player_tile,
            dungeon_offset,
            self.map.tiles
        )
        return self.enemies

    def recalibrate_buildings(self, zone_offsets: dict[str, tuple[int,int]] = None):
        """
        Vuelve a computar b.x, b.y de todos los edificios a partir de
        (zone, rel_tile_x, rel_tile_y) usando los zone_offsets.
        Si un edificio no tiene metadata, se deja tal cual.
        """
        zo = zone_offsets or ZONE_OFFSETS
        for b in self.buildings:
            if getattr(b, "zone", None) and getattr(b, "rel_tile_x", None) is not None:
                ox, oy = zo[b.zone]
                # tile coordinates absolutas
                tx = ox + b.rel_tile_x
                ty = oy + b.rel_tile_y
                # pixel coordinates
                b.x = tx * TILE_SIZE
                b.y = ty * TILE_SIZE
                # actualizar rect de colisión/renderer
                if hasattr(b, "rect"):
                    b.rect.topleft = (b.x, b.y)