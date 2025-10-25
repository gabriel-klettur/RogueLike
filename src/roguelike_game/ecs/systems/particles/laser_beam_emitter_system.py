import random
import time
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

import logging
logger = logging.getLogger(__name__)

class LaserBeamEmitterSystem:
    """
    Sistema ECS que emite partículas y aplica daño para cada entidad con LaserBeamComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log


    def update(self, world, camera=None):
        now = time.time()
        # Remove beam when middle mouse is released
        if not pygame.mouse.get_pressed()[1]:
            world.components.get('LaserBeamComponent', {}).clear()
            return
        # Debug beam presence
        beam_count = len(world.components.get('LaserBeamComponent', {}))
        if beam_count:
            logger.debug(f"[LaserBeamEmitter] frame={now:.3f} beams={beam_count}")
        to_remove = []
        for caster, beam in list(world.components.get('LaserBeamComponent', {}).items()):
            # dynamic thickness from beam.scale
            thickness_px = max(2, int(beam.scale * 20))
            half_thickness = thickness_px / 2
            # Recalculate beam origin/target to follow caster and cursor
            pos_cmp = world.components['Position'].get(caster)
            if pos_cmp:
                cx, cy = pos_cmp.x, pos_cmp.y
                sprite_cmp = world.components.get('Sprite', {}).get(caster)
                if sprite_cmp:
                    w, h = sprite_cmp.image.get_size()
                    cx += w/2; cy += h/2
                mx, my = pygame.mouse.get_pos()
                wx = mx / camera.zoom + camera.offset_x
                wy = my / camera.zoom + camera.offset_y
                beam.origin = (cx, cy)
                beam.target = (wx, wy)
            x1, y1 = beam.origin
            x2, y2 = beam.target
            dx = x2 - x1
            dy = y2 - y1
            length = (dx*dx + dy*dy) ** 0.5 or 1
            # Determine simulation_space and max_particles (optional)
            sim_space = getattr(beam, 'particle_simulation_space', None)
            if sim_space is None:
                sim_space = getattr(beam, 'simulation_space', None)
            anchor_local = isinstance(sim_space, str) and sim_space.lower() == 'local'
            max_particles = getattr(beam, 'max_particles', None)
            try:
                max_particles = int(max_particles) if max_particles is not None else None
            except Exception:
                max_particles = None
            if isinstance(sim_space, str) and sim_space.lower() not in ('local', 'world') and not getattr(beam, '_warned_simspace', False):
                try:
                    logger.warning("[LaserBeamEmitter] unknown simulation_space='%s' (expected 'local'|'world')", sim_space)
                except Exception:
                    pass
                setattr(beam, '_warned_simspace', True)
            if isinstance(max_particles, int) and max_particles <= 0 and not getattr(beam, '_warned_nonpos_max', False):
                try:
                    logger.warning("[LaserBeamEmitter] non-positive max_particles=%s ignored", max_particles)
                except Exception:
                    pass
                setattr(beam, '_warned_nonpos_max', True)
            # Compute current active anchored count for budget
            if anchor_local and isinstance(max_particles, int) and max_particles > 0:
                active = 0
                for pc in world.components.get('ParticleComponent', {}).values():
                    if getattr(pc, 'anchor_eid', None) == caster:
                        active += 1
                budget = max_particles - active
            else:
                budget = None
            # 1. Generar partículas a lo largo de la línea
            emit_n = int(getattr(beam, 'particle_count', 0) or 0)
            if isinstance(budget, int):
                emit_n = max(0, min(emit_n, budget))
            for i in range(emit_n):
                t = i / beam.particle_count
                px = x1 + t * dx + random.uniform(-beam.dispersion, beam.dispersion)
                py = y1 + t * dy + random.uniform(-beam.dispersion, beam.dispersion)
                pid = world.create_entity()
                world.components['Position'][pid] = Position(px, py)
                color = random.choice(beam.colors)
                size = thickness_px
                # beam particles live only one frame to avoid trails
                lifespan_frames = 1
                # Optional advanced fields on beam component
                blend_mode = getattr(beam, 'particle_blend_mode', None)
                size_ol = getattr(beam, 'particle_size_over_life', None)
                alpha_ol = getattr(beam, 'particle_alpha_over_life', None)
                color_ol = getattr(beam, 'particle_color_over_life', None)
                grav = getattr(beam, 'particle_gravity', None)
                drg = getattr(beam, 'particle_drag', None)
                anchor = caster if anchor_local else None
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    0, 0, color, size, lifespan_frames,
                    anchor_eid=anchor,
                    blend_mode=blend_mode,
                    size_over_life=size_ol,
                    alpha_over_life=alpha_ol,
                    color_over_life=color_ol,
                    gravity=grav,
                    drag=drg,
                )
            # 2. Aplicar daño a entidades en el haz (una vez por caster)
            for target in world.get_entities_with('Position', 'Health'):
                pos_t = world.components['Position'][target]
                sprite_t = world.components.get('Sprite', {}).get(target)
                if sprite_t:
                    tw, th = sprite_t.image.get_size()
                    tx = pos_t.x + tw / 2
                    ty = pos_t.y + th / 2
                    br = max(tw, th) / 2
                else:
                    tx = pos_t.x
                    ty = pos_t.y
                    br = 0
                tdx = tx - x1
                tdy = ty - y1
                proj = (tdx * dx + tdy * dy) / length
                # skip if outside extended segment
                if proj + br < 0 or proj - br > length:
                    continue
                pdist = abs(tdx * dy - tdy * dx) / length
                if pdist <= half_thickness + br:
                    # Respetar godmode: invulnerabilidad del jugador
                    is_player_target = target in world.components.get('PlayerTagComponent', {})
                    gm_target = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player_target
                    # One-shot si el caster es jugador y godmode activo
                    gm_attacker = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (caster in world.components.get('PlayerTagComponent', {}))
                    if not gm_target:
                        hp = world.components['Health'][target]
                        if gm_attacker:
                            hp.current_hp = 0
                        else:
                            hp.current_hp = max(0, hp.current_hp - beam.damage)

            # 3. Quitar componente si expiró la duración
            if beam.duration is not None and now >= beam.start_time + beam.duration:
                to_remove.append(caster)
        for caster in to_remove:
            world.components['LaserBeamComponent'].pop(caster, None)