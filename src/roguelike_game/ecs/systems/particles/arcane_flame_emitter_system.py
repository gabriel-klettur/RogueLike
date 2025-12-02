import random
import time
import math
import logging
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

class ArcaneFlameEmitterSystem:
    """
    Sistema ECS que emite partículas para cada ArcaneFlameComponent activo.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        now = time.time()
        for caster, comp in list(world.components.get('ArcaneFlameComponent', {}).items()):
            # no emitir tras expiración
            if now - comp.start_time >= comp.duration:
                continue
            pos_cmp = world.components.get('Position', {}).get(caster)
            if not pos_cmp:
                continue
            cx, cy = pos_cmp.x, pos_cmp.y
            # Optional simulation_space and max_particles
            sim_space = getattr(comp, 'particle_simulation_space', getattr(comp, 'simulation_space', None))
            anchor_local = isinstance(sim_space, str) and sim_space.lower() == 'local'
            max_particles = getattr(comp, 'max_particles', None)
            try:
                max_particles = int(max_particles) if max_particles is not None else None
            except Exception:
                max_particles = None
            logger = logging.getLogger(__name__)
            if isinstance(sim_space, str) and sim_space.lower() not in ('local', 'world') and not getattr(comp, '_warned_simspace', False):
                try:
                    logger.warning("[ArcaneFlameEmitter] unknown simulation_space='%s' (expected 'local'|'world')", sim_space)
                except Exception:
                    pass
                setattr(comp, '_warned_simspace', True)
            if isinstance(max_particles, int) and max_particles <= 0 and not getattr(comp, '_warned_nonpos_max', False):
                try:
                    logger.warning("[ArcaneFlameEmitter] non-positive max_particles=%s ignored", max_particles)
                except Exception:
                    pass
                setattr(comp, '_warned_nonpos_max', True)
            if anchor_local and isinstance(max_particles, int) and max_particles > 0:
                active = 0
                for pc in world.components.get('ParticleComponent', {}).values():
                    if getattr(pc, 'anchor_eid', None) == caster:
                        active += 1
                budget = max_particles - active
            else:
                budget = None
            emit_n = int(getattr(comp, 'particle_count', 0) or 0)
            if emit_n <= 0:
                emit_n = 1
            if isinstance(budget, int):
                emit_n = max(0, min(emit_n, budget))
            # Optional inherit velocity
            vel_cmp = world.components.get('Velocity', {}).get(caster)
            try:
                inherit_v = float(getattr(comp, 'inherit_velocity', 0.0) or 0.0)
            except Exception:
                inherit_v = 0.0
            # Advanced particle params (optional)
            blend_mode = getattr(comp, 'particle_blend_mode', None)
            size_ol = getattr(comp, 'particle_size_over_life', None)
            alpha_ol = getattr(comp, 'particle_alpha_over_life', None)
            color_ol = getattr(comp, 'particle_color_over_life', None)
            gval = getattr(comp, 'particle_gravity', None)
            if isinstance(gval, (int, float)):
                grav = (0.0, float(gval))
            elif isinstance(gval, (list, tuple)) and len(gval) >= 2:
                try:
                    grav = (float(gval[0]), float(gval[1]))
                except Exception:
                    grav = None
            else:
                grav = None
            dval = getattr(comp, 'particle_drag', None)
            drg = float(dval) if isinstance(dval, (int, float)) else None
            anchor = caster if anchor_local else None
            for _ in range(emit_n):
                angle = random.uniform(0, 2 * math.pi)
                r = random.uniform(0, comp.radius)
                px = cx + math.cos(angle) * r
                py = cy + math.sin(angle) * r
                vx = math.cos(angle) * comp.particle_speed
                vy = math.sin(angle) * comp.particle_speed
                if inherit_v and vel_cmp is not None:
                    fac = max(0.0, min(1.0, inherit_v))
                    vx += float(vel_cmp.vx) * fac
                    vy += float(vel_cmp.vy) * fac
                pid = world.create_entity()
                world.components['Position'][pid] = Position(px, py)
                size = random.randint(comp.size_range[0], comp.size_range[1])
                color = random.choice(comp.color_choices)
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    vx, vy, color, size, comp.particle_lifespan,
                    anchor_eid=anchor,
                    blend_mode=blend_mode,
                    size_over_life=size_ol,
                    alpha_over_life=alpha_ol,
                    color_over_life=color_ol,
                    gravity=grav,
                    drag=drg,
                )
