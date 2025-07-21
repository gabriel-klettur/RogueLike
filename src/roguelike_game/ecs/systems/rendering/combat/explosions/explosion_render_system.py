from roguelike_engine.utils.benchmark import benchmark

class ExplosionRenderSystem:
    """
    ECS system to render explosion effect models.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.ExplosionRenderSystem.update")
    def update(self, world, screen, camera):
        # Render each explosion model (no dirty rect tracking)
        for eid, comp in world.components.get('ExplosionComponent', {}).items():
            comp.model.render(screen, camera)
        return []
