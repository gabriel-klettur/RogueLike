import math
import time
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

import logging
logger = logging.getLogger(__name__)

class SlashEmitterSystem:
    """
    Sistema ECS que emite partículas para eventos de slash (cuchillada).
    Genera las partículas una sola vez cuando se crea el componente SlashEmitterComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.3.SlashEmitterSystem.update")
    def update(self, world, camera=None):
        now = time.time()
        emitters = world.components.get('SlashEmitterComponent', {})
        for caster, emitter in list(emitters.items()):
            logger.debug(f"[SlashEmitterSystem] caster={caster} count={emitter.count}")
            pos_cmp = world.components.get('Position', {}).get(caster)
            if not pos_cmp:
                continue
            cx, cy = pos_cmp.x, pos_cmp.y
            sprite_cmp = world.components.get('Sprite', {}).get(caster)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                cx += w / 2
                cy += h / 2
            # Parámetros de emisión
            radius = emitter.radius
            arc_range = emitter.arc_range
            count = emitter.count
            lifespan = emitter.lifespan
            size_min, size_max = emitter.size_range
            base_color = emitter.color
            speed_mult = emitter.speed_multiplier
            dir_x, dir_y = emitter.direction
            # Emitir partículas
            for i in range(count):
                t = (i / (count - 1)) - 0.5 if count > 1 else 0
                angle = math.atan2(dir_y, dir_x) + t * arc_range
                ox = math.cos(angle) * radius
                oy = math.sin(angle) * radius
                scale = 1 - abs(t) * 2
                speed = speed_mult * (1 + scale * 2)
                size = int(size_min + (size_max - size_min) * scale)
                color = base_color
                pid = world.create_entity()
                world.components['Position'][pid] = Position(cx + ox, cy + oy)
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    math.cos(angle) * speed,
                    math.sin(angle) * speed,
                    color,
                    size,
                    lifespan
                )
            # Remover emisor para que solo emita una vez
            world.components['SlashEmitterComponent'].pop(caster, None)
