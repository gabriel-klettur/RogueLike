import random
import logging
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

class LightningEmitterSystem:
    """
    ECS system that emits particles along the lightning path for LightningComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        # For each entity with a LightningComponent, emit particles at each lightning vertex
        logger = logging.getLogger(__name__)
        for eid, comp in world.components.get('LightningComponent', {}).items():
            model = comp.model
            pts = list(model.points)
            if not pts:
                continue
            emit_rate = max(1, int(getattr(comp, 'particle_emit_rate', 2)))
            speed = float(getattr(comp, 'particle_speed', 0.0) or 0.0)
            dispersion = float(getattr(comp, 'particle_dispersion', 0.0) or 0.0)
            size_min = getattr(comp, 'size_min', None)
            size_max = getattr(comp, 'size_max', None)
            palette = getattr(comp, 'colors_palette', None) if hasattr(comp, 'colors_palette') else None
            lifespan_frames = int(getattr(comp, 'particle_lifespan', 1) or 1)
            # Optional simulation space and cap
            sim_space = getattr(comp, 'particle_simulation_space', getattr(comp, 'simulation_space', None))
            anchor_local = isinstance(sim_space, str) and sim_space.lower() == 'local'
            max_particles = getattr(comp, 'max_particles', None)
            try:
                max_particles = int(max_particles) if max_particles is not None else None
            except Exception:
                max_particles = None
            if isinstance(sim_space, str) and sim_space.lower() not in ('local', 'world') and not getattr(comp, '_warned_simspace', False):
                try:
                    logger.warning("[LightningEmitter] unknown simulation_space='%s' (expected 'local'|'world')", sim_space)
                except Exception:
                    pass
                setattr(comp, '_warned_simspace', True)
            if isinstance(max_particles, int) and max_particles <= 0 and not getattr(comp, '_warned_nonpos_max', False):
                try:
                    logger.warning("[LightningEmitter] non-positive max_particles=%s ignored", max_particles)
                except Exception:
                    pass
                setattr(comp, '_warned_nonpos_max', True)
            if anchor_local and isinstance(max_particles, int) and max_particles > 0:
                active = 0
                for pc in world.components.get('ParticleComponent', {}).values():
                    if getattr(pc, 'anchor_eid', None) == eid:
                        active += 1
                remaining = max_particles - active
            else:
                remaining = None

            for i, (x, y) in enumerate(pts):
                # Base direction along the bolt
                if i < len(pts) - 1:
                    nx, ny = pts[i + 1]
                    dx, dy = (nx - x), (ny - y)
                else:
                    px = pts[i - 1][0] if i > 0 else x + 1
                    py = pts[i - 1][1] if i > 0 else y
                    dx, dy = (x - px), (y - py)
                # Normalize
                length = (dx * dx + dy * dy) ** 0.5 or 1.0
                bdx, bdy = dx / length, dy / length

                for _ in range(emit_rate):
                    if isinstance(remaining, int) and remaining <= 0:
                        break
                    # Jitter spawn pos slightly
                    sx = x + random.uniform(-1.5, 1.5)
                    sy = y + random.uniform(-1.5, 1.5)
                    pid = world.create_entity()
                    world.components.setdefault('Position', {})[pid] = Position(sx, sy)

                    # Color
                    if isinstance(palette, (list, tuple)) and palette:
                        try:
                            color = random.choice(palette)
                            color = tuple(int(max(0, min(255, c))) for c in color[:3])
                        except Exception:
                            color = (random.randint(80, 120), random.randint(180, 230), 255)
                    else:
                        color = (random.randint(80, 120), random.randint(180, 230), 255)

                    # Size: random in range if provided, otherwise fixed
                    if isinstance(size_min, int) and isinstance(size_max, int) and size_max >= size_min:
                        size = random.randint(size_min, size_max)
                    else:
                        size = int(getattr(comp, 'particle_size', 2) or 2)

                    # Velocity with angular dispersion around base direction
                    if speed > 0.0:
                        # Rotate base dir by random angle in [-dispersion, dispersion]
                        ang = random.uniform(-dispersion, dispersion)
                        ca = __import__('math').cos(ang)
                        sa = __import__('math').sin(ang)
                        vx = bdx * ca - bdy * sa
                        vy = bdx * sa + bdy * ca
                        dxp, dyp = vx * speed, vy * speed
                    else:
                        dxp, dyp = 0.0, 0.0

                    # Optional advanced particle params carried by LightningComponent
                    blend_mode = getattr(comp, 'particle_blend_mode', None)
                    size_ol = getattr(comp, 'particle_size_over_life', None)
                    alpha_ol = getattr(comp, 'particle_alpha_over_life', None)
                    color_ol = getattr(comp, 'particle_color_over_life', None)
                    grav = getattr(comp, 'particle_gravity', None)
                    drg = getattr(comp, 'particle_drag', None)
                    anchor = eid if anchor_local else None
                    world.components.setdefault('ParticleComponent', {})[pid] = ParticleComponent(
                        dxp, dyp, color, size, lifespan_frames,
                        anchor_eid=anchor,
                        blend_mode=blend_mode,
                        size_over_life=size_ol,
                        alpha_over_life=alpha_ol,
                        color_over_life=color_ol,
                        gravity=grav,
                        drag=drg,
                    )
                    if isinstance(remaining, int):
                        remaining -= 1
