# Path: src/roguelike_game/game/entities_manager.py
from types import SimpleNamespace
from roguelike_game.config_player import RENDERED_SPRITE_SIZE

from roguelike_game.game.map_manager import MapManager
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.systems.editor.buildings.model.persistence.load_buildings_from_json import load_buildings_from_json

class BuildingsManager:
    """
    Carga y mantiene edificios del juego.
    """
    
    def __init__(self, z_state, game_map: MapManager):
        self.z_state = z_state
        self.map = game_map
            
        self.buildings = []        

        self.init_buildings()    

    def init_buildings(self):
        """
        Carga edificios.
        Devuelve buildings.
        """        
        self.buildings = load_buildings_from_json(self.z_state)
        self.recalibrate_buildings()
        
        return self.buildings    

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
        Actualiza edificios.
        """

        # 1) Edificios
        @benchmark(perf_log, "2.1.buildings_update")
        def _update_buildings():
            for b in self.buildings:
                if hasattr(b, "update"):
                    b.update(state, game_map)
        _update_buildings()

    #! DEBERIA IR EN ECS
    @property
    def player(self):
        ecs_mgr = getattr(self, 'ecs_manager', None)
        if ecs_mgr:
            pos = ecs_mgr.ecs_world.player_position
            eid = getattr(ecs_mgr.ecs_world, 'player_entity', None)
            if pos and eid is not None:
                sprite_cmp = ecs_mgr.ecs_world.components.get('Sprite', {}).get(eid)
                if sprite_cmp and hasattr(sprite_cmp, 'image'):
                    sprite_size = sprite_cmp.image.get_size()
                else:
                    sprite_size = RENDERED_SPRITE_SIZE
                return SimpleNamespace(x=pos.x, y=pos.y, sprite_size=sprite_size)
        return None