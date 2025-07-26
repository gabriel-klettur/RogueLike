import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.sphere_magic_shield_component import SphereMagicShieldComponent

class SphereMagicShieldSystem:
    """
    ECS system to update sphere magic shield: pulses radius and expires component.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.SphereMagicShieldSystem.update")
    def update(self, world, camera=None):
        for eid, comp in list(world.components.get('SphereMagicShieldComponent', {}).items()):
            # Pulse radius
            t = comp.model.elapsed()
            pulse = math.sin(t * 4) * 0.1
            comp.model.radius = int(comp.model.base_radius * (1 + pulse))
            # Remove finished
            if comp.model.is_finished():
                world.components['SphereMagicShieldComponent'].pop(eid, None)
