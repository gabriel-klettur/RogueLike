import random
import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.config.spells_config import SPELLS
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
            if not pos or not vel:
                continue

            cfg = self._get_cfg(fcmp)
            # Defaults suaves si no hay configuración
            emit_rate = int(getattr(cfg, "emit_rate", 0) or cfg.get("emit_rate", 0) if cfg else 0)
            if emit_rate <= 0:
                # fallback: usar particle_count como tasa si está presente; si no, 2 por tick
                emit_rate = int((getattr(cfg, "particle_count", 0) or cfg.get("particle_count", 0)) if cfg else 0) or 2
            speed = float((getattr(cfg, "particle_speed", 0.8) or cfg.get("particle_speed", 0.8)) if cfg else 0.8)
            lifespan = int((getattr(cfg, "particle_lifespan", 25) or cfg.get("particle_lifespan", 25)) if cfg else 25)
            size_range = (2, 4)
            try:
                sr = (getattr(cfg, "size_range", None) or cfg.get("size_range", None)) if cfg else None
                if isinstance(sr, (list, tuple)) and len(sr) >= 2:
                    size_range = (int(sr[0]), int(sr[1]))
            except Exception:
                pass
            colors = [(255, 200, 120), (255, 170, 60), (255, 240, 160)]
            try:
                cc = (getattr(cfg, "particle_colors", None) or cfg.get("particle_colors", None)) if cfg else None
                if isinstance(cc, (list, tuple)) and cc:
                    colors = [tuple(map(int, c)) for c in cc]
            except Exception:
                pass
            dispersion = float((getattr(cfg, "particle_dispersion", 8.0) or cfg.get("particle_dispersion", 8.0)) if cfg else 8.0)

            # Dirección base: opuesta a la velocidad del proyectil
            base_angle = math.degrees(math.atan2(vel.vy, vel.vx)) + 180.0
            # Punto de spawn ligeramente detrás del centro
            spawn_x = pos.x - vel.vx * 0.2
            spawn_y = pos.y - vel.vy * 0.2

            for _ in range(int(max(1, emit_rate))):
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
