"""
Actualizador de edificios: maneja lógica de update.
"""
from roguelike_engine.utils.benchmark import benchmark

class BuildingsUpdater:
    """
    Actualiza la lógica de cada edificio.
    """
    def update(self, buildings, state, game_map, perf_log):
        @benchmark(perf_log, "2.1.buildings_update")
        def _update_buildings():
            for b in buildings:
                if hasattr(b, "update"):
                    b.update(state, game_map)
        _update_buildings()
