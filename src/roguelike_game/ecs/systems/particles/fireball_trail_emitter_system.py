import random
import math
import logging
from roguelike_engine.utils.benchmark import benchmark
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
            emit_n = int(max(1, emit_rate))
            # Log detallado solo en los primeros frames de cada fireball para no hacer ruido
            try:
                if getattr(fcmp, 'age', 0) <= 2:
                    self._logger.debug(
                        "[FireballTrailEmitter] eid=%s age=%s emit_n=%d speed=%.2f life=%s size=%s colors=%d disp=%.1f pos=(%.1f,%.1f) vel=(%.2f,%.2f)",
                        eid, getattr(fcmp, 'age', None), emit_n, float(speed), str(lifespan), str(size_range), len(colors), float(dispersion), pos.x, pos.y, float(vx), float(vy)
                    )
            except Exception:
                pass

            for _ in range(emit_n):
                ang = math.radians(base_angle + random.uniform(-dispersion, dispersion))
                spd = random.uniform(0.4 * speed, 1.0 * speed)
                dx = math.cos(ang) * spd
                dy = math.sin(ang) * spd
                size = random.randint(size_range[0], size_range[1])
                color = random.choice(colors)

                peid = world.create_entity()
                comps.setdefault("Position", {})[peid] = Position(
                    spawn_x + random.uniform(-1.5, 1.5),
                    spawn_y + random.uniform(-1.5, 1.5),
                )
                comps.setdefault("ParticleComponent", {})[peid] = ParticleComponent(dx, dy, color, size, lifespan)
