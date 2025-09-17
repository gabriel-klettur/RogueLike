from roguelike_engine.utils.benchmark import benchmark
import logging
logger = logging.getLogger(__name__)

class ParticleSystem:
    """
    ECS system to update particles: moves them and expires by lifespan.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        # Obtener componentes de posición y partículas
        positions = world.components.get('Position', {})
        particles = world.components.get('ParticleComponent', {})
        # One-time debug of particle count to help diagnose emission
        if not getattr(self, '_dbg_logged_particles', False):
            setattr(self, '_dbg_logged_particles', True)
            try:
                logger.debug("[ParticleSystem] start update: particles=%d", len(particles))
            except Exception:
                pass
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