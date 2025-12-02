import random
import math
import logging
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.config.particles_config import get_preset
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent


class FireballTrailEmitterSystem:
    """
    Emite partículas de estela para cada entidad con `FireballComponent`.

    Los parámetros de emisión se leen del config del hechizo (spells.json) a través
    de las claves aplanadas en `SpellConfig`:
      - emit_rate (int): partículas por tick.
      - particle_speed (float): velocidad base de las partículas.
      - particle_lifespan (int): vida en ticks.
      - size_range (list[int,int]): rango de tamaños.
      - particle_colors (list[tuple[int,int,int]]): paleta de colores.
      - particle_dispersion (float): dispersión angular en grados alrededor de la
        dirección opuesta a la velocidad de la fireball.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._dbg_logged = False
        self._logger = logging.getLogger(__name__)

    def _get_cfg(self, comp):
        if not getattr(comp, "spell_key", None):
            return None
        try:
            return SPELLS.get(comp.spell_key)
        except Exception:
            return None

    def update(self, world, camera=None):
        comps = world.components
        positions = comps.get("Position", {})
        velocities = comps.get("Velocity", {})
        fireballs = comps.get("FireballComponent", {})

        for eid, fcmp in list(fireballs.items()):
            pos = positions.get(eid)
            vel = velocities.get(eid)
            if not pos:
                continue

            cfg = self._get_cfg(fcmp)
            # Resolver preset + overrides desde SPELLS (fallback a claves aplanadas)
            base: dict = {}
            overrides: dict = {}
            if cfg is not None:
                # 1) Obtener preset por id: desde cfg.vfx si es string, o desde vfx_obj['preset'] si es dict
                preset_id = None
                try:
                    vfx_attr = getattr(cfg, "vfx", None)
                    if isinstance(vfx_attr, str):
                        preset_id = vfx_attr
                    elif isinstance(vfx_attr, dict):
                        preset_id = vfx_attr.get("preset") if isinstance(vfx_attr.get("preset"), str) else None
                except Exception:
                    preset_id = None
                if preset_id is None:
                    try:
                        vfx_obj_tmp = getattr(cfg, "extra", {}).get("vfx")
                        if isinstance(vfx_obj_tmp, dict):
                            pid = vfx_obj_tmp.get("preset")
                            if isinstance(pid, str):
                                preset_id = pid
                    except Exception:
                        pass
                if isinstance(preset_id, str):
                    try:
                        p = get_preset(preset_id)
                        if p and isinstance(getattr(p, "vfx", None), dict):
                            pv = p.vfx.get("particles")
                            if isinstance(pv, dict):
                                base = dict(pv)
                        if not self._dbg_logged:
                            self._logger.debug("[FireballTrailEmitterSystem] resolved preset_id=%s base=%s", preset_id, str(bool(base)))
                    except Exception:
                        pass
                # 2) Overrides declarados en el objeto vfx del spell (ya sea cfg.vfx o extra.vfx)
                try:
                    vfx_obj = None
                    vfx_attr = getattr(cfg, "vfx", None)
                    if isinstance(vfx_attr, dict):
                        vfx_obj = vfx_attr
                    else:
                        vfx_obj = getattr(cfg, "extra", {}).get("vfx")
                    if isinstance(vfx_obj, dict):
                        pov = vfx_obj.get("particles")
                        if isinstance(pov, dict):
                            overrides = dict(pov)
                except Exception:
                    pass
            parts = {**base, **overrides}

            if not self._dbg_logged:
                self._dbg_logged = True
                try:
                    self._logger.debug("[FireballTrailEmitterSystem] active. fireballs=%d preset=%s overrides=%s", len(fireballs), str(bool(base)), str(bool(overrides)))
                except Exception:
                    pass

            # Defaults suaves si no hay configuración en preset/overrides
            emit_rate = parts.get("emit_rate") if isinstance(parts.get("emit_rate"), int) else None
            if not isinstance(emit_rate, int) or emit_rate <= 0:
                cnt = parts.get("count") if isinstance(parts.get("count"), int) else None
                if isinstance(cnt, int) and cnt > 0:
                    emit_rate = max(1, min(8, cnt // 2))
                else:
                    emit_rate = int((getattr(cfg, "particle_count", 0) or (cfg.get("particle_count", 0) if cfg else 0))) if cfg else 0
                    if emit_rate <= 0:
                        emit_rate = 2

            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else None
            if not isinstance(speed, (int, float)):
                speed = float((getattr(cfg, "particle_speed", 0.8) or (cfg.get("particle_speed", 0.8) if cfg else 0.8)) if cfg else 0.8)

            lifespan = parts.get("lifespan") if isinstance(parts.get("lifespan"), int) else None
            if not isinstance(lifespan, int):
                lifespan = int((getattr(cfg, "particle_lifespan", 25) or (cfg.get("particle_lifespan", 25) if cfg else 25)) if cfg else 25)

            size_range = (2, 4)
            try:
                sr = parts.get("size_range")
                if not (isinstance(sr, (list, tuple)) and len(sr) >= 2):
                    sr = (getattr(cfg, "size_range", None) or (cfg.get("size_range", None) if cfg else None)) if cfg else None
                if isinstance(sr, (list, tuple)) and len(sr) >= 2:
                    size_range = (int(sr[0]), int(sr[1]))
            except Exception:
                pass

            # Aplicar multiplicadores por escala visual del proyectil
            try:
                scale_mul = float(getattr(fcmp, 'vfx_scale_multiplier', 1.0))
            except Exception:
                scale_mul = 1.0
            # Grosor: escalar el tamaño de partícula linealmente con la escala visual
            if scale_mul and abs(scale_mul - 1.0) > 1e-3:
                smin = max(1, int(round(size_range[0] * max(0.25, min(8.0, scale_mul)))))
                smax = max(smin, int(round(size_range[1] * max(0.25, min(8.0, scale_mul)))))
                size_range = (smin, smax)
                # Cantidad: escalar emit_rate con un clamp conservador para evitar desbordes
                emit_rate = int(max(1, min(64, round(emit_rate * max(0.5, min(6.0, scale_mul))))))

            colors = [(255, 200, 120), (255, 170, 60), (255, 240, 160)]
            try:
                if isinstance(parts.get("color"), (list, tuple)) and len(parts.get("color")) >= 3:
                    c = parts.get("color")
                    colors = [tuple(map(int, c[:3]))]
                elif isinstance(parts.get("colors"), (list, tuple)) and parts.get("colors"):
                    colors = [tuple(map(int, c[:3])) for c in parts.get("colors") if isinstance(c, (list, tuple)) and len(c) >= 3]
                else:
                    cc = (getattr(cfg, "particle_colors", None) or (cfg.get("particle_colors", None) if cfg else None)) if cfg else None
                    if isinstance(cc, (list, tuple)) and cc:
                        colors = [tuple(map(int, c[:3])) for c in cc if isinstance(c, (list, tuple)) and len(c) >= 3]
            except Exception:
                pass

            dispersion = parts.get("dispersion") if isinstance(parts.get("dispersion"), (int, float)) else None
            if not isinstance(dispersion, (int, float)):
                dispersion = float((getattr(cfg, "particle_dispersion", 8.0) or (cfg.get("particle_dispersion", 8.0) if cfg else 8.0)) if cfg else 8.0)

            # Advanced particle params (optional)
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            size_ol = parts.get("size_over_life") if isinstance(parts.get("size_over_life"), (list, tuple)) else None
            alpha_ol = parts.get("alpha_over_life") if isinstance(parts.get("alpha_over_life"), (list, tuple)) else None
            color_ol = parts.get("color_over_life") if isinstance(parts.get("color_over_life"), (list, tuple)) else None
            gval = parts.get("gravity")
            if isinstance(gval, (int, float)):
                grav = (0.0, float(gval))
            elif isinstance(gval, (list, tuple)) and len(gval) >= 2:
                try:
                    grav = (float(gval[0]), float(gval[1]))
                except Exception:
                    grav = None
            else:
                grav = None
            dval = parts.get("drag")
            drg = float(dval) if isinstance(dval, (int, float)) else None

            # Dirección base: opuesta a la velocidad del proyectil (con fallback a FireballComponent)
            vx = getattr(vel, "vx", None) if vel is not None else None
            vy = getattr(vel, "vy", None) if vel is not None else None
            if not isinstance(vx, (int, float)) or not isinstance(vy, (int, float)):
                vx, vy = getattr(fcmp, "dx", 0.0), getattr(fcmp, "dy", 0.0)
            # si la velocidad es casi nula, forzar una dirección fija hacia atrás
            if abs(vx) + abs(vy) < 1e-3:
                vx, vy = -1.0, 0.0
            base_angle = math.degrees(math.atan2(vy, vx)) + 180.0
            # Punto de spawn ligeramente detrás del centro (usar vx,vy robustos)
            spawn_x = pos.x - float(vx) * 0.2
            spawn_y = pos.y - float(vy) * 0.2
            # Optional simulation space/cap/inherit
            sim_space = parts.get("simulation_space") if isinstance(parts.get("simulation_space"), str) else getattr(fcmp, 'particle_simulation_space', getattr(fcmp, 'simulation_space', None))
            anchor_local = isinstance(sim_space, str) and sim_space.lower() == 'local'
            max_particles = parts.get("max_particles") if isinstance(parts.get("max_particles"), int) else getattr(fcmp, 'max_particles', None)
            try:
                max_particles = int(max_particles) if max_particles is not None else None
            except Exception:
                max_particles = None
            if isinstance(sim_space, str) and sim_space.lower() not in ('local', 'world') and not getattr(fcmp, '_warned_simspace', False):
                try:
                    self._logger.warning("[FireballTrailEmitter] unknown simulation_space='%s' (expected 'local'|'world')", sim_space)
                except Exception:
                    pass
                setattr(fcmp, '_warned_simspace', True)
            if isinstance(max_particles, int) and max_particles <= 0 and not getattr(fcmp, '_warned_nonpos_max', False):
                try:
                    self._logger.warning("[FireballTrailEmitter] non-positive max_particles=%s ignored", max_particles)
                except Exception:
                    pass
                setattr(fcmp, '_warned_nonpos_max', True)
            if anchor_local and isinstance(max_particles, int) and max_particles > 0:
                active = 0
                for pc in comps.get('ParticleComponent', {}).values():
                    if getattr(pc, 'anchor_eid', None) == eid:
                        active += 1
                budget = max_particles - active
            else:
                budget = None
            emit_n = int(max(1, emit_rate))
            if isinstance(budget, int):
                emit_n = max(0, min(emit_n, budget))
            # Log detallado solo en los primeros frames de cada fireball para no hacer ruido
            try:
                if getattr(fcmp, 'age', 0) <= 2:
                    self._logger.debug(
                        "[FireballTrailEmitter] eid=%s age=%s emit_n=%d speed=%.2f life=%s size=%s colors=%d disp=%.1f pos=(%.1f,%.1f) vel=(%.2f,%.2f)",
                        eid, getattr(fcmp, 'age', None), emit_n, float(speed), str(lifespan), str(size_range), len(colors), float(dispersion), pos.x, pos.y, float(vx), float(vy)
                    )
            except Exception:
                pass

            inherit_v = 0.0
            try:
                inherit_v = float(parts.get("inherit_velocity")) if isinstance(parts.get("inherit_velocity"), (int, float)) else float(getattr(fcmp, 'inherit_velocity', 0.0) or 0.0)
            except Exception:
                inherit_v = 0.0
            for _ in range(emit_n):
                ang = math.radians(base_angle + random.uniform(-dispersion, dispersion))
                spd = random.uniform(0.4 * speed, 1.0 * speed)
                dx = math.cos(ang) * spd
                dy = math.sin(ang) * spd
                if inherit_v and vel is not None:
                    fac = max(0.0, min(1.0, inherit_v))
                    try:
                        dx += float(vel.vx) * fac
                        dy += float(vel.vy) * fac
                    except Exception:
                        pass
                size = random.randint(size_range[0], size_range[1])
                color = random.choice(colors)

                peid = world.create_entity()
                comps.setdefault("Position", {})[peid] = Position(
                    spawn_x + random.uniform(-1.5, 1.5),
                    spawn_y + random.uniform(-1.5, 1.5),
                )
                anchor = eid if anchor_local else None
                comps.setdefault("ParticleComponent", {})[peid] = ParticleComponent(
                    dx, dy, color, size, lifespan,
                    anchor_eid=anchor,
                    blend_mode=blend_mode,
                    size_over_life=size_ol,
                    alpha_over_life=alpha_ol,
                    color_over_life=color_ol,
                    gravity=grav,
                    drag=drg,
                )
