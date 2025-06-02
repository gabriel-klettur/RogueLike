import random, math, time, pygame
from pygame.math import Vector2
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

class HealingAuraEmitterSystem:
    """
    Emite partículas de aura mientras el AuraComponent esté activo.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "HealingAuraEmitterSystem.update")
    def update(self, world, camera=None):
        now = time.time()
        for caster, aura in world.components.get('AuraComponent', {}).items():
            # calcular posición central del caster
            pos_cmp = world.components['Position'][caster]
            cx, cy = pos_cmp.x, pos_cmp.y

            # centrar partículas en el centro del sprite
            sprite_cmp = world.components.get('Sprite', {}).get(caster)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                cx += w/2
                cy += h/2

            # extra_velocity inversa al movimiento actual del caster
            vel_cmp = world.components.get('Velocity', {}).get(caster)
            if vel_cmp:
                dirv = Vector2(vel_cmp.vx, vel_cmp.vy)
                if dirv.length() > 0:
                    dirv = dirv.normalize()
                extra = -0.5 * dirv
            else:
                extra = Vector2(0, 0)

            # emitir partículas
            for _ in range(aura.particles_per_frame):
                angle = random.random() * 2 * math.pi
                base_dx = math.cos(angle) * aura.particle_speed
                base_dy = math.sin(angle) * aura.particle_speed
                dx, dy = base_dx + extra.x, base_dy + extra.y

                size  = random.randint(aura.particle_min_size, aura.particle_max_size)
                color = random.choice(aura.particle_colors)

                pid = world.create_entity()
                world.components['Position'][pid] = Position(
                    cx + getattr(aura, 'offset_x', 0), cy
                )
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    dx, dy, color, size, aura.particle_lifespan
                )
