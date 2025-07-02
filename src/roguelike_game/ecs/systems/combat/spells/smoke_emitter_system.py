from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.smoke_emitter_component import SmokeEmitterComponent

class SmokeEmitterSystem:
    """
    ECS system that updates SmokeEmitterComponent by advancing its legacy SmokeEmitterModel.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.SmokeEmitterSystem.update")
    def update(self, world, camera=None):
        # Update all smoke emitter models
        for eid, comp in list(world.components.get('SmokeEmitterComponent', {}).items()):
            comp.model.update()
