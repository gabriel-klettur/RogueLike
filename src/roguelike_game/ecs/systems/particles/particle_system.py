from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.utils.particle_pool import get_particle_pool
import logging
logger = logging.getLogger(__name__)

class ParticleSystem:
    """
    ECS system to update particles: moves them and expires by lifespan.
    Optimizado con particle pooling para reducir allocations.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        self._particle_pool = None
        self._curves_validated: set = set()  # Cache de entidades ya validadas
    
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
            # One-time curve validation warnings (usando set en lugar de atributo)
            if eid not in self._curves_validated:
                self._curves_validated.add(eid)
                self._validate_curves(comp)
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
            # Física opcional: gravedad y drag (retrocompatible si no están definidos)
            try:
                gx = float(getattr(comp, 'gx', 0.0) or 0.0)
                gy = float(getattr(comp, 'gy', 0.0) or 0.0)
            except Exception:
                gx, gy = 0.0, 0.0
            try:
                drag = float(getattr(comp, 'drag', 0.0) or 0.0)
            except Exception:
                drag = 0.0
            if drag < 0.0:
                drag = 0.0
            if drag > 0.98:
                drag = 0.98
            # Integración simple por tick
            comp.dx += gx
            comp.dy += gy
            if drag > 0.0:
                comp.dx *= (1.0 - drag)
                comp.dy *= (1.0 - drag)
            pos.x += comp.dx
            pos.y += comp.dy
            # Envejecer y expirar
            comp.age += 1
            if comp.age >= comp.lifespan:
                # Liberar al pool en lugar de destruir
                self._curves_validated.discard(eid)
                try:
                    if self._particle_pool is None:
                        self._particle_pool = get_particle_pool(world)
                    self._particle_pool.release(eid)
                except Exception:
                    # Fallback a remove_entity si el pool falla
                    world.remove_entity(eid)
    
    def _validate_curves(self, comp) -> None:
        """Valida curvas de partícula una sola vez."""
        for name in ('size_over_life', 'alpha_over_life', 'color_over_life'):
            curve = getattr(comp, name, None)
            if not isinstance(curve, (list, tuple)):
                continue
            last_t = -1e9
            bad = False
            for pt in curve:
                try:
                    t = float(pt[0])
                except Exception:
                    bad = True
                    continue
                if not (0.0 <= t <= 1.0) or t < last_t:
                    bad = True
                last_t = t
            if bad:
                try:
                    logger.warning("[ParticleSystem] curve '%s' unsorted/out-of-range", name)
                except Exception:
                    pass