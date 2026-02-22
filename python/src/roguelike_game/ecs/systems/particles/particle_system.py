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
        
        # Early exit if no particles
        if not particles:
            return
            
        # One-time debug of particle count to help diagnose emission
        if not getattr(self, '_dbg_logged_particles', False):
            self._dbg_logged_particles = True
            logger.debug("[ParticleSystem] start update: particles=%d", len(particles))
        
        # Collect expired particles to remove after iteration (avoid dict mutation during iteration)
        expired: list = []
        curves_validated = self._curves_validated
        
        for eid, comp in particles.items():
            pos = positions.get(eid)
            if pos is None:
                expired.append(eid)
                continue
            
            # One-time curve validation warnings
            if eid not in curves_validated:
                curves_validated.add(eid)
                self._validate_curves(comp)
            
            # Anchor tracking (for particles attached to moving entities)
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
                        pos.x += anchor_pos.x - last_x
                        pos.y += anchor_pos.y - last_y
                        comp.anchor_last_x = anchor_pos.x
                        comp.anchor_last_y = anchor_pos.y
            
            # Physics: gravity and drag (use getattr with defaults, avoid try/except in hot loop)
            gx = getattr(comp, 'gx', 0.0) or 0.0
            gy = getattr(comp, 'gy', 0.0) or 0.0
            drag = getattr(comp, 'drag', 0.0) or 0.0
            
            # Clamp drag
            if drag < 0.0:
                drag = 0.0
            elif drag > 0.98:
                drag = 0.98
            
            # Simple integration
            dx = comp.dx + gx
            dy = comp.dy + gy
            if drag > 0.0:
                factor = 1.0 - drag
                dx *= factor
                dy *= factor
            comp.dx = dx
            comp.dy = dy
            pos.x += dx
            pos.y += dy
            
            # Age and expire
            comp.age += 1
            if comp.age >= comp.lifespan:
                expired.append(eid)
        
        # Process expired particles outside the loop
        if expired:
            if self._particle_pool is None:
                self._particle_pool = get_particle_pool(world)
            pool = self._particle_pool
            for eid in expired:
                curves_validated.discard(eid)
                try:
                    pool.release(eid)
                except Exception:
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