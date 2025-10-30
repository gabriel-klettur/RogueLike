import time
import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.utils.health_utils import is_neutral
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import get_entity_center


class ForceFieldSystem:
    """
    Aplica una fuerza a entidades cercanas modificando su Velocity.
    - mode: 'pull' atrae hacia el centro; 'push' empuja hacia afuera.
    - force: magnitud de impulso agregado por frame (px/frame) sobre la dirección normalizada.
    - duration: TTL del campo.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'ForceFieldSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        fields = world.components.get('ForceFieldComponent', {})
        if not fields:
            return
        pos_map = world.components.get('Position', {})
        vel_map = world.components.setdefault('Velocity', {})
        hp_map = world.components.get('Health', {})
        cs_map = world.components.get('CombatStats', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})

        to_remove = []
        for fid, field in list(fields.items()):
            # Expirar
            try:
                if getattr(field, 'duration', 0.0) > 0.0 and now >= float(field.start_time) + float(field.duration):
                    to_remove.append(fid)
                    continue
            except Exception:
                pass
            cpos = pos_map.get(fid)
            if cpos is None:
                continue
            # Seguir al ancla (e.g., caster) si está configurado
            try:
                if getattr(field, 'follow', False) and getattr(field, 'anchor_eid', None) is not None:
                    ax, ay = get_entity_center(world, int(field.anchor_eid))
                    cpos.x = float(ax)
                    cpos.y = float(ay)
            except Exception:
                pass
            cx, cy = float(cpos.x), float(cpos.y)
            radius = float(getattr(field, 'radius', 0.0))
            r2 = radius * radius
            force = float(getattr(field, 'force', 0.0))
            mode = (getattr(field, 'mode', 'pull') or 'pull').lower()
            owner = getattr(field, 'owner', None)
            affect_owner = bool(getattr(field, 'affect_owner', False))
            affect_allies = bool(getattr(field, 'affect_allies', False))
            affect_neutrals = bool(getattr(field, 'affect_neutrals', False))
            affect_enemies = bool(getattr(field, 'affect_enemies', True))
            try:
                drag = float(getattr(field, 'drag', 0.0) or 0.0)
            except Exception:
                drag = 0.0
            if drag < 0.0:
                drag = 0.0
            if drag > 0.98:
                drag = 0.98

            # Precompute owner faction (if any)
            owner_fac = None
            try:
                if owner is not None:
                    oid = world.components.get('Identity', {}).get(owner)
                    owner_fac = getattr(oid, 'faction', None) if oid else None
            except Exception:
                owner_fac = None

            if radius <= 0 or force <= 0:
                continue

            # Aplicar a entidades vivas con Health o CombatStats (y con Position). Filtros por relación opcionales.
            target_ids = set(hp_map.keys()) | set(cs_map.keys())
            for eid in list(target_ids):
                # Dueño
                if eid == owner and not affect_owner:
                    continue
                if eid in dead_map or eid in dying_map:
                    continue
                # Neutrales
                if is_neutral(world, eid) and not affect_neutrals:
                    continue
                tpos = pos_map.get(eid)
                if tpos is None:
                    continue
                # Aliados/enemigos (si hay dueño con facción)
                if owner_fac is not None and eid != owner:
                    try:
                        tid = world.components.get('Identity', {}).get(eid)
                        tfac = getattr(tid, 'faction', None) if tid else None
                        if tfac is not None:
                            if tfac == owner_fac and not affect_allies:
                                continue
                            if tfac != owner_fac and not affect_enemies:
                                continue
                    except Exception:
                        pass
                tx, ty = float(tpos.x), float(tpos.y)
                dx = tx - cx
                dy = ty - cy
                d2 = dx*dx + dy*dy
                if d2 > r2 + 1e-6:
                    continue
                # Dirección normalizada; si d<1, evitar división por cero
                dl = math.sqrt(max(d2, 1e-8))
                nx = dx / dl
                ny = dy / dl
                if mode == 'pull':
                    nx, ny = -nx, -ny
                # Atenuar en borde para suavidad: escala por (1 - d/r)
                att = 1.0 - min(1.0, dl / radius)
                dvx = nx * force * att
                dvy = ny * force * att
                v = vel_map.get(eid)
                if v is None:
                    # Lazy creation de Velocity si hace falta
                    try:
                        from roguelike_game.ecs.components.transform.velocity import Velocity
                        v = Velocity(0.0, 0.0)
                        vel_map[eid] = v
                    except Exception:
                        continue
                # Aplicar amortiguación opcional antes de sumar impulso
                if drag > 0.0:
                    v.vx *= (1.0 - drag)
                    v.vy *= (1.0 - drag)
                v.vx += dvx
                v.vy += dvy
        for fid in to_remove:
            fields.pop(fid, None)
            # limpiar Position si no lo usan otros sistemas visuales
            world.components.get('Position', {}).pop(fid, None)
