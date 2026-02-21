from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.abilities.smoke_emitter_component import SmokeEmitterComponent

class SmokeEmitterSystem:
    """
    ECS system that updates SmokeEmitterComponent by advancing its legacy SmokeEmitterModel.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        # Update all smoke emitter models
        for eid, comp in list(world.components.get('SmokeEmitterComponent', {}).items()):
            comp.model.update()
