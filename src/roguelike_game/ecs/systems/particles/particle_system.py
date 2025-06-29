# Path: src/roguelike_game/ecs/systems/particles/particle_system.py
from roguelike_engine.utils.benchmark import benchmark

class ParticleSystem:
    """
    ECS system to update particles: moves them and expires by lifespan.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2. ParticleSystem.update")
    def update(self, world, camera=None):
        # Obtener componentes de posición y partículas
        positions = world.components.get('Position', {})
        particles = world.components.get('ParticleComponent', {})
        for eid, comp in list(particles.items()):
            pos = positions.get(eid)
            if pos is None:
                world.remove_entity(eid)
                continue
            # Mover partícula
            pos.x += comp.dx
            pos.y += comp.dy
            # Envejecer y expirar
            comp.age += 1
            if comp.age >= comp.lifespan:
                world.remove_entity(eid)