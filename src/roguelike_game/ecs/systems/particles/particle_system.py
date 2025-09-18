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
            # Si la partícula está anclada a una entidad (ej. jugador en un slash),
            # trasladarla por el delta de movimiento del ancla antes de aplicar su propia velocidad.
            anchor_id = getattr(comp, 'anchor_eid', None)
            if anchor_id is not None:
                anchor_pos = positions.get(anchor_id)
                if anchor_pos is not None:
                    last_x = getattr(comp, 'anchor_last_x', None)
                    last_y = getattr(comp, 'anchor_last_y', None)
                    if last_x is None or last_y is None:
                        comp.anchor_last_x = anchor_pos.x
                        comp.anchor_last_y = anchor_pos.y
                    else:
                        dx_anchor = anchor_pos.x - last_x
                        dy_anchor = anchor_pos.y - last_y
                        pos.x += dx_anchor
                        pos.y += dy_anchor
                        comp.anchor_last_x = anchor_pos.x
                        comp.anchor_last_y = anchor_pos.y
            # Mover partícula
            pos.x += comp.dx
            pos.y += comp.dy
            # Envejecer y expirar
            comp.age += 1
            if comp.age >= comp.lifespan:
                world.remove_entity(eid)