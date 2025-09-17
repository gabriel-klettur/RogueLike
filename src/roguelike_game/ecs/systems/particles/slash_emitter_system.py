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
            # Alinear centro de emisión con el centro real del hitbox (mismo offset del resolver)
            try:
                off = float(getattr(emitter, 'offset', 0.0))
            except Exception:
                off = 0.0
            base_cx = cx + dir_x * off
            base_cy = cy + dir_y * off
            # Emitir partículas
            for i in range(count):
                t = (i / (count - 1)) - 0.5 if count > 1 else 0
                angle = math.atan2(dir_y, dir_x) + t * arc_range
                # Perfil de tamaño/velocidad a lo largo del arco
                scale = max(0.0, 1 - abs(t) * 2)  # clamp para evitar tamaños negativos
                speed = float(speed_mult) * (1 + scale * 2)
                size = int(size_min + (size_max - size_min) * scale)
                # Mantener partículas contenidas dentro del radio durante toda su vida
                # Distancia máxima radial recorrida ~ speed * lifespan. Restar además medio tamaño + margen visual.
                travel = speed * lifespan
                margin = max(1.0, (size / 2.0))
                spawn_r = max(0.0, float(radius) - travel - margin)
                # Coordenadas polares -> cartesianas
                ox = math.cos(angle) * spawn_r
                oy = math.sin(angle) * spawn_r
                color = base_color
                pid = world.create_entity()
                world.components['Position'][pid] = Position(base_cx + ox, base_cy + oy)
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    math.cos(angle) * speed,
                    math.sin(angle) * speed,
                    color,
                    size,
                    lifespan
                )
            # Remover emisor para que solo emita una vez
            world.components['SlashEmitterComponent'].pop(caster, None)
