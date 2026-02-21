"""
Actualizador de edificios: maneja lógica de update.
"""
from roguelike_engine.utils.benchmark.benchmark import benchmark

class BuildingsUpdater:
    """
    Actualiza la lógica de cada edificio.
    """
    def update(self, buildings, state, game_map, perf_log):        
        def _update_buildings():
            for b in buildings:
                if hasattr(b, "update"):
                    b.update(state, game_map)
        _update_buildings()
