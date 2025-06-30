# Path: src/roguelike_game/game/buildings_manager.py



from roguelike_game.managers.map_manager import MapManager


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
