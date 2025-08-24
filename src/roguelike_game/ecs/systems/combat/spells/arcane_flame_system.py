
from roguelike_engine.utils.benchmark import benchmark

class ArcaneFlameSystem:
    """
    Sistema ECS que expira el componente ArcaneFlameComponent tras su duración.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        for eid, comp in list(world.components.get('ArcaneFlameComponent', {}).items()):
            comp.model.update()
            if comp.model.is_finished():
                world.components['ArcaneFlameComponent'].pop(eid, None)
