# Path: src/roguelike_game/game/entities_manager.py
from typing import Tuple, List

from roguelike_game.entities.load_entities import load_entities
from roguelike_game.entities.load_hostile import load_hostile
from roguelike_game.game.map_manager import MapManager
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.controller.map_service import calculate_dungeon_offset
from roguelike_game.config_entities import ENEMY_MAX_UPDATE_DISTANCE
from roguelike_game.entities.npc.types.elite.controller import EliteController
from roguelike_engine.utils.benchmark import benchmark

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
        # Calcular offset en tiles de la dungeon
        lob_x, lob_y = self.map.lobby_offset
        dungeon_offset = calculate_dungeon_offset((lob_x, lob_y))
        # Spawn procedural de enemigos
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

    def recalibrate_buildings(self):
        """
        Actualiza el rect de colisión/render de cada edificio,
        usando las propiedades x,y derivadas de rel_x/rel_y y zone.
        """
        for b in self.buildings:
            if getattr(b, "zone", None) is not None and getattr(b, "rel_x", None) is not None:
                abs_x, abs_y = b.x, b.y
                if hasattr(b, "rect"):
                    b.rect.topleft = (abs_x, abs_y)

    def update(self, state, game_map, systems, perf_log):
        """
        Actualiza todas las entidades de la partida:
          - Jugador
          - Obstáculos
          - Edificios
          - Enemigos (normales y élites)
        Cada sección está medida para perfilado.
        """

        # 1) Jugador
        @benchmark(perf_log, "2.1.player_update")
        def _update_player():
            self.player.update(game_map)
        _update_player()

        # 2) Obstáculos
        @benchmark(perf_log, "2.2.obstacles_update")
        def _update_obstacles():
            for ob in self.obstacles:
                if hasattr(ob, "update"):
                    ob.update(state, game_map)
        _update_obstacles()

        # 3) Edificios
        @benchmark(perf_log, "2.3.buildings_update")
        def _update_buildings():
            for b in self.buildings:
                if hasattr(b, "update"):
                    b.update(state, game_map)
        _update_buildings()

        # 4) Filtrado de enemigos por distancia y tipo
        @benchmark(perf_log, "2.4.filter_enemies")
        def _filter_enemies():
            normals, elites = [], []
            px, py = self.player.x, self.player.y
            max_d2 = ENEMY_MAX_UPDATE_DISTANCE ** 2
            for e in self.enemies:
                dx, dy = e.x - px, e.y - py
                if dx*dx + dy*dy > max_d2:
                    continue
                ctrl = getattr(e, "controller", None)
                if isinstance(ctrl, EliteController):
                    elites.append(ctrl)
                else:
                    normals.append(e)
            return normals, elites
        normals, elites = _filter_enemies()

        # 5) Enemigos normales
        @benchmark(perf_log, "2.5.normals_update")
        def _update_normals():
            for e in normals:
                e.update(state, game_map, self)
        _update_normals()

        # 6) Enemigos élite
        @benchmark(perf_log, "2.6.elites_update")
        def _update_elites():
            for ctrl in elites:
                ctrl.update(state, game_map, self, systems.effects, systems.explosions)
        _update_elites()
