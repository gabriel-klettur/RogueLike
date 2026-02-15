import math
import time
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

import logging
logger = logging.getLogger(__name__)
try:
    logger.setLevel(logging.INFO)
except Exception:
    pass

class SlashEmitterSystem:
    """
    Sistema ECS que emite partículas para eventos de slash (cuchillada).
    Genera las partículas una sola vez cuando se crea el componente SlashEmitterComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        emitters = world.components.get('SlashEmitterComponent', {})
        if not emitters:
            return
        for caster, emitter in list(emitters.items()):
            if logger.isEnabledFor(logging.DEBUG):
                try:
                    logger.debug(
                        "[SlashEmitterSystem] begin caster=%s radius=%s arc=%.1fdeg count=%s",
                        caster,
                        getattr(emitter, 'radius', None),
                        math.degrees(getattr(emitter, 'arc_range', 0.0) or 0.0),
                        getattr(emitter, 'count', None),
                    )
                except Exception:
                    pass
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
            # Optional cap when anchoring locally: respect max_particles
            sim_space = getattr(emitter, 'particle_simulation_space', getattr(emitter, 'simulation_space', None))
            anchor_local = isinstance(sim_space, str) and sim_space.lower() == 'local'
            max_particles = getattr(emitter, 'max_particles', None)
            try:
                max_particles = int(max_particles) if max_particles is not None else None
            except Exception:
                max_particles = None
            if isinstance(sim_space, str) and sim_space.lower() not in ('local', 'world') and not getattr(emitter, '_warned_simspace', False):
                try:
                    logger.warning("[SlashEmitter] unknown simulation_space='%s' (expected 'local'|'world')", sim_space)
                except Exception:
                    pass
                setattr(emitter, '_warned_simspace', True)
            if isinstance(max_particles, int) and max_particles <= 0 and not getattr(emitter, '_warned_nonpos_max', False):
                try:
                    logger.warning("[SlashEmitter] non-positive max_particles=%s ignored", max_particles)
                except Exception:
                    pass
                setattr(emitter, '_warned_nonpos_max', True)
            budget = None
            if anchor_local and isinstance(max_particles, int) and max_particles > 0:
                active = 0
                for pc in world.components.get('ParticleComponent', {}).values():
                    if getattr(pc, 'anchor_eid', None) == caster:
                        active += 1
                budget = max_particles - active
            emit_n = count
            if isinstance(budget, int):
                emit_n = max(0, min(count, budget))
            # Emitir partículas
            emitted = 0
            for i in range(emit_n):
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
                # Optional advanced params from emitter component (dynamic attributes supported)
                blend_mode = getattr(emitter, 'particle_blend_mode', None)
                size_ol = getattr(emitter, 'particle_size_over_life', None)
                alpha_ol = getattr(emitter, 'particle_alpha_over_life', None)
                color_ol = getattr(emitter, 'particle_color_over_life', None)
                grav = getattr(emitter, 'particle_gravity', None)
                drg = getattr(emitter, 'particle_drag', None)
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    math.cos(angle) * speed,
                    math.sin(angle) * speed,
                    color,
                    size,
                    lifespan,
                    anchor_eid=caster,
                    blend_mode=blend_mode,
                    size_over_life=size_ol,
                    alpha_over_life=alpha_ol,
                    color_over_life=color_ol,
                    gravity=grav,
                    drag=drg,
                )
                emitted += 1
            if logger.isEnabledFor(logging.DEBUG):
                try:
                    logger.debug("[SlashEmitterSystem] emitted=%d particles for caster=%s", emitted, caster)
                except Exception:
                    pass
            # Remover emisor para que solo emita una vez
            world.components['SlashEmitterComponent'].pop(caster, None)
