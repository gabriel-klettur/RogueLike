from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.smoke_component import SmokeComponent

class SmokeSystem:
    """
    ECS system to update smoke effects and expire components when done.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        for eid, comp in list(world.components.get('SmokeComponent', {}).items()):
            # Update each smoke particle
            for p in comp.model.particles:
                p.update()
            # Remove dead particles
            comp.model.particles = [p for p in comp.model.particles if not p.is_dead()]
            # If no particles remain, remove component
            if not comp.model.particles:
                world.components['SmokeComponent'].pop(eid, None)
