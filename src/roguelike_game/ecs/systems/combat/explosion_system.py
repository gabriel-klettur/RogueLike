# Path: src/roguelike_game/ecs/systems/combat/explosion_system.py
from roguelike_engine.utils.benchmark import benchmark

class ExplosionSystem:
    """
    ECS system to update explosion effect models and remove entities when finished.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.ExplosionSystem.update")
    def update(self, world, camera=None):
        # Update each explosion model
        for eid, comp in list(world.components.get('ExplosionComponent', {}).items()):
            comp.model.update()
            if getattr(comp.model, 'finished', False):
                world.remove_entity(eid)
