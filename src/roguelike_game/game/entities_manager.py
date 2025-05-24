# Path: src/roguelike_game/game/entities_manager.py
from typing import Tuple, List

from roguelike_game.entities.load_entities import load_entities
from roguelike_game.game.map_manager import MapManager
from roguelike_engine.utils.benchmark import benchmark

class EntitiesManager:
    """
    Carga y mantiene entidades del juego: jugador, obstáculos y edificios.
    """
    
    def __init__(self, z_state, game_map: MapManager):
        self.z_state = z_state
        self.map = game_map
        
        self.player = None
        self.obstacles = []
        self.buildings = []        

        self.init_statics()    

    def init_statics(self):
        """
        Carga jugador, obstáculos y edificios.
        Devuelve (player, obstacles, buildings).
        """        
        self.player, self.obstacles, self.buildings = load_entities(self.z_state)
        self.recalibrate_buildings()
        
        return self.player, self.obstacles, self.buildings    

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
          # NPCs gestionados por ECS; eliminados de este método
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