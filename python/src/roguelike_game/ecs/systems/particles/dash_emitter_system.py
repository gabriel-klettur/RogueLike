import random
import math
import logging
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_game.ecs.utils.collider_utils import build_collider_rect

logger = logging.getLogger(__name__)

class DashEmitterSystem:
    """
    ECS system that emits dash trail particles for entities with DashComponent.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        for eid, dash in list(world.components.get('DashComponent', {}).items()):
            pos_cmp = world.components.get('Position', {}).get(eid)
            if not pos_cmp:
                continue
            # Determine feet collider center for spawn
            multi = world.components.get('MultiCollider', {}).get(eid)
            feet_rect = None
            if multi:
                feet = multi.colliders.get('feet')
                if feet:
                    feet_rect = build_collider_rect(pos_cmp.x, pos_cmp.y, feet)
            if feet_rect:
                px, py = feet_rect.center
            else:
                px, py = pos_cmp.x, pos_cmp.y
            base_angle = math.degrees(math.atan2(dash.dir_y, dash.dir_x))
            # Optional max_particles when using local simulation_space
            sim_space = getattr(dash, 'particle_simulation_space', None)
            anchor_local = isinstance(sim_space, str) and sim_space.lower() == 'local'
            max_particles = getattr(dash, 'max_particles', None)
            try:
                max_particles = int(max_particles) if max_particles is not None else None
            except Exception:
                max_particles = None
            if isinstance(max_particles, int) and max_particles <= 0 and not getattr(dash, '_warned_nonpos_max', False):
                try:
                    logger.warning("[DashEmitter] non-positive max_particles=%s ignored", max_particles)
                except Exception:
                    pass
                setattr(dash, '_warned_nonpos_max', True)
            budget = None
            if anchor_local and isinstance(max_particles, int) and max_particles > 0:
                active = 0
                for pc in world.components.get('ParticleComponent', {}).values():
                    if getattr(pc, 'anchor_eid', None) == eid:
                        active += 1
                budget = max_particles - active
            emit_n = 2
            if isinstance(budget, int):
                emit_n = max(0, min(emit_n, budget))
            for _ in range(emit_n):
                angle = math.radians(base_angle + 180 + random.uniform(-30, 30))
                speed = random.uniform(1, 3)
                dx = math.cos(angle) * speed
                dy = math.sin(angle) * speed
                # inherit velocity (optional)
                try:
                    inh = float(getattr(dash, 'inherit_velocity', 0.0) or 0.0)
                except Exception:
                    inh = 0.0
                if inh:
                    vcmp = world.components.get('Velocity', {}).get(eid)
                    if vcmp is not None:
                        dx += float(vcmp.vx) * max(0.0, min(1.0, inh))
                        dy += float(vcmp.vy) * max(0.0, min(1.0, inh))
                color = random.choice([(200,200,255),(150,150,255),(255,255,255)])
                size = random.randint(3,6)
                lifespan = 15
                peid = world.create_entity()
                world.components.setdefault('Position', {})[peid] = Position(
                    px + random.uniform(-5, 5), py + random.uniform(-5, 5)
                )
                # Optional advanced params from DashComponent if present
                blend_mode = getattr(dash, 'particle_blend_mode', None)
                size_ol = getattr(dash, 'particle_size_over_life', None)
                alpha_ol = getattr(dash, 'particle_alpha_over_life', None)
                color_ol = getattr(dash, 'particle_color_over_life', None)
                grav = getattr(dash, 'particle_gravity', None)
                drg = getattr(dash, 'particle_drag', None)
                # simulation_space (optional): local -> anchor to dash owner entity
                anchor = eid if isinstance(sim_space, str) and sim_space.lower() == 'local' else None
                world.components.setdefault('ParticleComponent', {})[peid] = ParticleComponent(
                    dx, dy, color, size, lifespan,
                    anchor_eid=anchor,
                    blend_mode=blend_mode,
                    size_over_life=size_ol,
                    alpha_over_life=alpha_ol,
                    color_over_life=color_ol,
                    gravity=grav,
                    drag=drg,
                )
