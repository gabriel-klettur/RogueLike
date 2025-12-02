from roguelike_engine.utils.benchmark.benchmark import benchmark
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
            # One-time curve validation warnings (lightweight)
            if not getattr(comp, '_validated_curves', False):
                def _warn_curve(name, curve):
                    if not isinstance(curve, (list, tuple)):
                        return
                    last_t = -1e9
                    bad = False
                    for pt in curve:
                        try:
                            t = float(pt[0])
                        except Exception:
                            bad = True
                            continue
                        if not (0.0 <= t <= 1.0):
                            bad = True
                        if t < last_t:
                            bad = True
                        last_t = t
                    if bad:
                        try:
                            logger.warning("[ParticleSystem] curve '%s' unsorted/out-of-range; expected t in [0,1] ascending", name)
                        except Exception:
                            pass
                _warn_curve('size_over_life', getattr(comp, 'size_over_life', None))
                _warn_curve('alpha_over_life', getattr(comp, 'alpha_over_life', None))
                _warn_curve('color_over_life', getattr(comp, 'color_over_life', None))
                setattr(comp, '_validated_curves', True)
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
                world.remove_entity(eid)