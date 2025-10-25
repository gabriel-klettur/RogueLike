import random
import time
import math
import logging

from pygame.math import Vector2
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent

logger = logging.getLogger(__name__)

class HealingAuraEmitterSystem:
    """
    Sistema ECS que emite partículas visuales para cada entidad que posea un AuraComponent.
    Cada partícula nace en un punto aleatorio dentro de un óvalo que cubre la altura y anchura del sprite,
    y asciende de manera vertical hasta la altura de la cabeza, donde desaparece.
    Esto crea la ilusión de un flujo de energía curativa que recorre todo el cuerpo del caster.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        now = time.time()
        for caster, aura in world.components.get('AuraComponent', {}).items():
            # 1. Obtener componente Position del caster
            pos_cmp = world.components.get('Position', {}).get(caster)
            if not pos_cmp:
                continue
            base_x = pos_cmp.x
            base_y = pos_cmp.y

            # 2. Determinar dimensiones del sprite (si existe) o usar valores estimados
            sprite_cmp = world.components.get('Sprite', {}).get(caster)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                cx = base_x + w / 2            # Centro horizontal del sprite
                feet_y = base_y + h            # Y de los pies (parte inferior)
                head_y = base_y                # Y de la cabeza (parte superior)
                half_width = w / 2
                half_height = h / 2
                # Centro del óvalo: mitad de camino entre cabeza y pies
                ellipse_cy = head_y + half_height
            else:
                # Si no hay sprite, definimos parámetros arbitrarios
                cx = base_x
                feet_y = base_y
                head_y = base_y - 32
                half_width = 16
                half_height = 16
                ellipse_cy = head_y + half_height
                w = half_width * 2
                h = half_height * 2

            # 3. Calcular velocidad extra inversa al movimiento para inercia suave
            vel_cmp = world.components.get('Velocity', {}).get(caster)
            if vel_cmp:
                dirv = Vector2(vel_cmp.vx, vel_cmp.vy)
                if dirv.length() > 0:
                    dirv = dirv.normalize()
                extra = -0.5 * dirv
            else:
                extra = Vector2(0, 0)

            # Resolve optional AAA emission params (compatible defaults)
            buf = getattr(aura, 'buff', {}) or {}
            em_shape = buf.get('emission_shape') if isinstance(buf.get('emission_shape'), str) else getattr(aura, 'emission_shape', None)
            em_extent = buf.get('emission_extent', getattr(aura, 'emission_extent', None))
            em_dir = buf.get('emission_direction', getattr(aura, 'emission_direction', None))
            try:
                angle_spread_deg = float(buf.get('emission_angle_spread_deg', getattr(aura, 'emission_angle_spread_deg', 0.0)))
            except Exception:
                angle_spread_deg = 0.0
            try:
                speed_variance = float(buf.get('speed_variance', getattr(aura, 'speed_variance', 0.0)))
            except Exception:
                speed_variance = 0.0
            try:
                life_jitter = float(buf.get('lifetime_jitter', getattr(aura, 'lifetime_jitter', 0.0)))
            except Exception:
                life_jitter = 0.0
            size_start = buf.get('size_start', getattr(aura, 'size_start', None))
            # Lightweight validation warnings (once per aura)
            if isinstance(em_shape, str):
                shp = em_shape.lower()
                known = {"point", "circle", "ring", "line", "box", "cone"}
                if shp not in known and not getattr(aura, '_warned_bad_shape', False):
                    try:
                        logger.warning("[HealingAuraEmitter] unknown emission_shape='%s'", em_shape)
                    except Exception:
                        pass
                    setattr(aura, '_warned_bad_shape', True)
                # extent sanity
                try:
                    if shp in ("circle", "cone"):
                        if isinstance(em_extent, (int, float)) and float(em_extent) < 0 and not getattr(aura, '_warned_neg_extent', False):
                            logger.warning("[HealingAuraEmitter] negative extent radius for shape=%s: %s", shp, em_extent)
                            setattr(aura, '_warned_neg_extent', True)
                    if shp == "ring":
                        if isinstance(em_extent, (list, tuple)) and len(em_extent) >= 2:
                            if float(em_extent[0]) > float(em_extent[1]) and not getattr(aura, '_warned_ring_inner_outer', False):
                                logger.warning("[HealingAuraEmitter] ring extent inner>outer: %s", em_extent)
                                setattr(aura, '_warned_ring_inner_outer', True)
                    if shp == "box":
                        if isinstance(em_extent, (list, tuple)) and len(em_extent) >= 2:
                            if (float(em_extent[0]) <= 0 or float(em_extent[1]) <= 0) and not getattr(aura, '_warned_box_nonpos', False):
                                logger.warning("[HealingAuraEmitter] non-positive box extent: %s", em_extent)
                                setattr(aura, '_warned_box_nonpos', True)
                except Exception:
                    pass

            # Parse bursts (optional) from buff once and keep state on component
            bursts_def = buf.get('bursts') if isinstance(buf.get('bursts'), (list, tuple)) else None
            if bursts_def and not hasattr(aura, '_burst_events_ms'):
                evs = []
                loop = False
                for ev in bursts_def:
                    try:
                        if isinstance(ev, dict):
                            t = float(ev.get('time_s', ev.get('time', 0.0)))
                            c = int(ev.get('count', 0))
                            if ev.get('loop') is True:
                                loop = True
                        else:
                            t = float(ev[0]); c = int(ev[1])
                        if c > 0 and t >= 0.0:
                            evs.append((int(t * 1000.0), c))
                    except Exception:
                        continue
                evs.sort(key=lambda x: x[0])
                aura._burst_events_ms = evs
                aura._burst_loop = loop
                aura._burst_cursor = 0
                aura._burst_start_ms = int(now * 1000)
            # Determine extra count from due bursts this frame
            emit_extra = 0
            if hasattr(aura, '_burst_events_ms') and aura._burst_events_ms:
                last_ms = aura._burst_events_ms[-1][0]
                # wrap for looped bursts
                if getattr(aura, '_burst_loop', False) and last_ms > 0:
                    while int(now * 1000) - aura._burst_start_ms >= last_ms:
                        aura._burst_start_ms += last_ms
                        aura._burst_cursor = 0
                elapsed = int(now * 1000) - aura._burst_start_ms
                while aura._burst_cursor < len(aura._burst_events_ms) and elapsed >= aura._burst_events_ms[aura._burst_cursor][0]:
                    emit_extra += max(0, int(aura._burst_events_ms[aura._burst_cursor][1]))
                    aura._burst_cursor += 1
            # simulation_space and max_particles
            sim_space = buf.get('simulation_space', getattr(aura, 'particle_simulation_space', None))
            anchor_local = isinstance(sim_space, str) and sim_space.lower() == 'local'
            max_particles = buf.get('max_particles', getattr(aura, 'max_particles', None))
            try:
                max_particles = int(max_particles) if max_particles is not None else None
            except Exception:
                max_particles = None
            if isinstance(sim_space, str) and sim_space.lower() not in ('local', 'world') and not getattr(aura, '_warned_simspace', False):
                try:
                    logger.warning("[HealingAuraEmitter] unknown simulation_space='%s' (expected 'local'|'world')", sim_space)
                except Exception:
                    pass
                setattr(aura, '_warned_simspace', True)
            if isinstance(max_particles, int) and max_particles <= 0 and not getattr(aura, '_warned_nonpos_max', False):
                try:
                    logger.warning("[HealingAuraEmitter] non-positive max_particles=%s ignored", max_particles)
                except Exception:
                    pass
                setattr(aura, '_warned_nonpos_max', True)
            # current active anchored particles
            if anchor_local and isinstance(max_particles, int) and max_particles > 0:
                active = 0
                for pc in world.components.get('ParticleComponent', {}).values():
                    if getattr(pc, 'anchor_eid', None) == caster:
                        active += 1
                budget = max_particles - active
            else:
                budget = None
            emit_count = aura.particles_per_frame + emit_extra
            if isinstance(budget, int):
                emit_count = max(0, min(emit_count, budget))
            # Inherit velocity factor (optional)
            try:
                inherit_v = float(buf.get('inherit_velocity', getattr(aura, 'inherit_velocity', 0.0)) or 0.0)
            except Exception:
                inherit_v = 0.0

            # 4. Emitir partículas en cada frame (con bursts y caps)
            for _ in range(int(emit_count)):
                # 4.1. Muestreo según emission_shape (fallback a óvalo)
                dx_ell = 0.0
                dy_ell = 0.0
                spawn_x = cx
                spawn_y = ellipse_cy
                shape = str(em_shape).lower() if isinstance(em_shape, str) else None
                if shape == 'point':
                    spawn_x = cx + aura.offset_x
                    spawn_y = ellipse_cy
                elif shape == 'line':
                    # Extent: span across X (px or fraction of sprite width)
                    span = None
                    if isinstance(em_extent, (int, float)):
                        span = float(em_extent)
                        if 0.0 < span <= 1.0:
                            span = (w) * span
                    if span is None:
                        span = w
                    half_span = max(2.0, float(span) / 2.0)
                    dx_ell = random.uniform(-half_span, half_span)
                    spawn_x = cx + dx_ell + aura.offset_x
                    spawn_y = ellipse_cy
                elif shape == 'box':
                    # em_extent: [w,h] (px) o fracción del sprite si <=1
                    ex = ey = None
                    if isinstance(em_extent, (list, tuple)) and len(em_extent) >= 2:
                        try:
                            ex = float(em_extent[0]); ey = float(em_extent[1])
                        except Exception:
                            ex = ey = None
                    # derive half-box from extents or fallback to sprite half dims
                    bx = (ex / 2.0) if ex else half_width
                    by = (ey / 2.0) if ey else half_height
                    dx_ell = random.uniform(-bx, bx)
                    dy_ell = random.uniform(-by, by)
                    spawn_x = cx + dx_ell + aura.offset_x
                    spawn_y = ellipse_cy + dy_ell
                elif shape == 'circle':
                    # em_extent: radius (px) o fracción si <=1
                    if isinstance(em_extent, (int, float)):
                        r = float(em_extent)
                        if 0.0 < r <= 1.0:
                            r = min(half_width, half_height) * r
                    elif isinstance(em_extent, (list, tuple)) and len(em_extent) >= 1:
                        r = float(em_extent[0])
                    else:
                        r = float(min(half_width, half_height))
                    ang = random.random() * 2 * 3.14159
                    rr = random.uniform(0.0, r)
                    spawn_x = cx + math.cos(ang) * rr + aura.offset_x
                    spawn_y = ellipse_cy + math.sin(ang) * rr
                elif shape == 'ring':
                    # em_extent: [inner_radius, outer_radius]
                    if isinstance(em_extent, (list, tuple)) and len(em_extent) >= 2:
                        rin = max(0.0, float(em_extent[0])); rout = max(rin, float(em_extent[1]))
                    else:
                        rin = float(min(half_width, half_height) * 0.6)
                        rout = float(min(half_width, half_height))
                    ang = random.random() * 2 * 3.14159
                    rr = random.uniform(rin, rout)
                    spawn_x = cx + math.cos(ang) * rr + aura.offset_x
                    spawn_y = ellipse_cy + math.sin(ang) * rr
                elif shape == 'cone':
                    # Sector along emission_direction with spread; extent as radius
                    if isinstance(em_extent, (int, float)):
                        radius = float(em_extent)
                    elif isinstance(em_extent, (list, tuple)) and len(em_extent) >= 1:
                        radius = float(em_extent[0])
                    else:
                        radius = float(min(half_width, half_height) * 0.6)
                    base = Vector2(0.0, -1.0)
                    if isinstance(em_dir, (list, tuple)) and len(em_dir) >= 2:
                        try:
                            base.update(float(em_dir[0]), float(em_dir[1]))
                        except Exception:
                            base.update(0.0, -1.0)
                    if base.length_squared() == 0:
                        base.update(0.0, -1.0)
                    base = base.normalize()
                    spr = float(angle_spread_deg) * (3.14159 / 180.0)
                    ang = random.uniform(-spr, spr)
                    ca = math.cos(ang); sa = math.sin(ang)
                    vx0 = base.x * ca - base.y * sa
                    vy0 = base.x * sa + base.y * ca
                    rr = random.uniform(0.0, radius)
                    spawn_x = cx + vx0 * rr + aura.offset_x
                    spawn_y = ellipse_cy + vy0 * rr
                else:
                    # óvalo por rechazo (comportamiento previo)
                    for _tries in range(8):
                        dx_ell = random.uniform(-half_width, half_width)
                        dy_ell = random.uniform(-half_height, half_height)
                        if (dx_ell / half_width) ** 2 + (dy_ell / half_height) ** 2 <= 1:
                            break
                    spawn_x = cx + dx_ell + aura.offset_x
                    spawn_y = ellipse_cy + dy_ell

                # Asegurar que el punto de origen nunca esté por encima de la cabeza ni por debajo de los pies
                spawn_y = max(head_y, min(feet_y, spawn_y))

                # 4.2. Dirección y velocidad desde emission_direction y angle spread (fallback vertical)
                if isinstance(em_dir, (list, tuple)) and len(em_dir) >= 2:
                    try:
                        bx, by = float(em_dir[0]), float(em_dir[1])
                    except Exception:
                        bx, by = 0.0, -1.0
                else:
                    bx, by = 0.0, -1.0
                base = Vector2(bx, by)
                if base.length_squared() == 0:
                    base.update(0.0, -1.0)
                base = base.normalize()
                spr = float(angle_spread_deg) * (3.14159 / 180.0)
                if spr > 0.0:
                    ang = random.uniform(-spr, spr)
                    ca = math.cos(ang); sa = math.sin(ang)
                    vx0 = base.x * ca - base.y * sa
                    vy0 = base.x * sa + base.y * ca
                    vdir = Vector2(vx0, vy0)
                else:
                    vdir = base
                # speed variance (multiplicativo)
                var = max(-0.95, min(0.95, float(speed_variance))) if isinstance(speed_variance, (int, float)) else 0.0
                spd = abs(aura.particle_speed) * (1.0 + random.uniform(-var, var))
                if spd <= 0.0:
                    spd = 1.0
                vx = vdir.x * spd + extra.x
                vy = vdir.y * spd + extra.y
                # inherit_velocity from caster
                if inherit_v and vel_cmp:
                    fac = max(0.0, min(1.0, inherit_v))
                    vx += float(vel_cmp.vx) * fac
                    vy += float(vel_cmp.vy) * fac

                # 4.3. Color y tamaño aleatorios dentro de parámetros del aura
                if isinstance(size_start, (int, float)):
                    size = max(1, int(size_start))
                elif isinstance(size_start, (list, tuple)) and size_start:
                    try:
                        size = max(1, int(sum(float(v) for v in size_start[:2]) / min(2, len(size_start))))
                    except Exception:
                        size = random.randint(aura.particle_min_size, aura.particle_max_size)
                else:
                    size  = random.randint(aura.particle_min_size, aura.particle_max_size)
                color = random.choice(aura.particle_colors)

                # 4.4. Calcular lifespan de la partícula para que desaparezca al llegar a la cabeza
                #      dist_vertical = spawn_y - head_y (distancia desde punto de origen hasta la cabeza)
                dist_vertical = spawn_y - head_y
                #      Frames necesarios = dist_vertical / |-vy|
                if abs(vy) > 0:
                    frames_to_head = int(dist_vertical / abs(vy))
                else:
                    frames_to_head = aura.particle_lifespan
                #      No exceder lifespan máximo definido
                lifespan_frames = min(frames_to_head, aura.particle_lifespan)
                # lifetime_jitter: tratar <1 como ratio y >=1 como frames
                lj = float(life_jitter)
                if lj != 0.0:
                    if 0.0 < abs(lj) < 1.0:
                        jit = int(abs(lj) * lifespan_frames)
                    else:
                        jit = int(abs(lj))
                    delta = random.randint(-jit, jit)
                    lifespan_frames = max(6, min(aura.particle_lifespan, lifespan_frames + delta))

                # 4.5. Crear entidad de partícula en ECS
                pid = world.create_entity()
                world.components['Position'][pid] = Position(spawn_x, spawn_y)
                # Advanced particle params (optional) from AuraComponent.buff or attributes
                blend_mode = buf.get('particle_blend_mode') if isinstance(buf.get('particle_blend_mode'), str) else getattr(aura, 'particle_blend_mode', None)
                size_ol = buf.get('size_over_life') if isinstance(buf.get('size_over_life'), (list, tuple)) else getattr(aura, 'particle_size_over_life', None)
                alpha_ol = buf.get('alpha_over_life') if isinstance(buf.get('alpha_over_life'), (list, tuple)) else getattr(aura, 'particle_alpha_over_life', None)
                color_ol = buf.get('color_over_life') if isinstance(buf.get('color_over_life'), (list, tuple)) else getattr(aura, 'particle_color_over_life', None)
                gval = buf.get('particle_gravity', getattr(aura, 'particle_gravity', None))
                if isinstance(gval, (int, float)):
                    grav = (0.0, float(gval))
                elif isinstance(gval, (list, tuple)) and len(gval) >= 2:
                    try:
                        grav = (float(gval[0]), float(gval[1]))
                    except Exception:
                        grav = None
                else:
                    grav = None
                dval = buf.get('particle_drag', getattr(aura, 'particle_drag', None))
                drg = float(dval) if isinstance(dval, (int, float)) else None
                # Anchor to caster if simulation_space == local
                anchor = caster if anchor_local else None
                world.components['ParticleComponent'][pid] = ParticleComponent(
                    vx, vy, color, size, lifespan_frames,
                    anchor_eid=anchor,
                    blend_mode=blend_mode,
                    size_over_life=size_ol,
                    alpha_over_life=alpha_ol,
                    color_over_life=color_ol,
                    gravity=grav,
                    drag=drg,
                )