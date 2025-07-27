from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.firework_launch_component import FireworkLaunchComponent

class FireworkLaunchSystem:
    """
    Sistema ECS que actualiza el modelo de lanzamiento de fuegos artificiales y expira el componente al finalizar.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.FireworkLaunchSystem.update")
    def update(self, world, camera=None):
        for eid, comp in list(world.components.get('FireworkLaunchComponent', {}).items()):
            comp.model.update()
            if comp.model.finished:
                world.components['FireworkLaunchComponent'].pop(eid, None)
