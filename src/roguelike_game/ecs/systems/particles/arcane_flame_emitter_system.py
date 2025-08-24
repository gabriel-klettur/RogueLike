import random
import time
import math
from roguelike_engine.utils.benchmark import benchmark
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
            for _ in range(comp.particle_count):
                angle = random.uniform(0, 2 * math.pi)
                r = random.uniform(0, comp.radius)
                px = cx + math.cos(angle) * r
                py = cy + math.sin(angle) * r
                vx = math.cos(angle) * comp.particle_speed
                vy = math.sin(angle) * comp.particle_speed
                pid = world.create_entity()
                world.components['Position'][pid] = Position(px, py)
                size = random.randint(comp.size_range[0], comp.size_range[1])
                color = random.choice(comp.color_choices)
                world.components['ParticleComponent'][pid] = ParticleComponent(vx, vy, color, size, comp.particle_lifespan)
