import math
import time
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.combat.hitbox import HitboxComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import get_entity_center, mouse_world, direction_from_to, spawn_at_offset


class ConeBreathSystem:
    """
    Sistema que actualiza ConeBreathComponent: cada tick genera una Hitbox de arco
    que sigue al caster y aplica daño por tick. Opcionalmente, puede disparar
    un emisor de partículas de tipo slash para VFX temporales.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'ConeBreathSystem.update')
    def update(self, world, camera=None):
        comps = world.components.get('ConeBreathComponent', {})
        if not comps:
            return
        now = time.time()
        pos_map = world.components.get('Position', {})
        for eid, comp in list(comps.items()):
            # Expirar
            try:
                dur = float(getattr(comp, 'duration', 0.0) or 0.0)
                st = float(getattr(comp, 'start_time', 0.0) or 0.0)
                if dur > 0.0 and now >= st + dur:
                    comps.pop(eid, None)
                    # Limpieza básica de Position si no es usada por otros sistemas
                    world.components.get('Position', {}).pop(eid, None)
                    continue
            except Exception:
                pass
            # Primer tick inmediato o por periodo
            last = float(getattr(comp, 'last_tick_time', 0.0) or 0.0)
            tper = max(0.01, float(getattr(comp, 'tick_period', 0.2) or 0.2))
            if last != 0.0 and (now - last) < tper:
                continue
            comp.last_tick_time = now
            # Datos del cono
            arc_deg = float(getattr(comp, 'arc_degrees', 0.0) or 0.0)
            arc_rad = math.radians(max(1.0, arc_deg))
            length = float(getattr(comp, 'length', 0.0) or 0.0)
            dmg = float(getattr(comp, 'damage_per_tick', 0.0) or 0.0)
            owner = getattr(comp, 'owner', None)
            if owner is None:
                continue
            # Centro del caster
            cx, cy = get_entity_center(world, int(owner))
            # Dirección base: fija si se proporcionó initial_direction (NPC),
            # si no, usar mouse como en Slash/Hitbox rotate_with_owner
            dir_xy = getattr(comp, 'initial_direction', None)
            if isinstance(dir_xy, (list, tuple)) and len(dir_xy) >= 2:
                dx, dy = float(dir_xy[0]), float(dir_xy[1])
                mag = max(1e-6, (dx*dx + dy*dy) ** 0.5)
                dir_x, dir_y = dx / mag, dy / mag
                rotate_with_owner = False
            else:
                # Dirección hacia mouse; HitboxSystem actualizará si rotate_with_owner=True
                if camera is not None:
                    wx, wy = mouse_world(camera)
                else:
                    wx, wy = cx, cy
                dir_x, dir_y, _ = direction_from_to(cx, cy, wx, wy)
                rotate_with_owner = bool(getattr(comp, 'rotate_with_owner', True))
            follow_owner = bool(getattr(comp, 'follow_owner', True))
            offset = float(getattr(comp, 'offset', 0.0) or 0.0)
            # Spawnear entidad de hitbox efímera (1-2 frames) para aplicar daño
            hb_id = world.create_entity()
            sx, sy = spawn_at_offset(cx, cy, dir_x, dir_y, offset)
            pos_map[hb_id] = Position(float(sx), float(sy))
            lifespan_frames = 2  # suficiente para procesarse en el mismo frame por HitboxSystem
            world.components.setdefault('HitboxComponent', {})[hb_id] = HitboxComponent(
                owner=int(owner),
                offset=float(offset + length * 0.25),  # empuja el centro un poco hacia adelante
                radius=float(length),
                arc_angle=float(arc_rad),
                direction=(float(dir_x), float(dir_y)),
                lifespan=int(lifespan_frames),
                damage=float(dmg),
                follow_owner=bool(follow_owner),
                rotate_with_owner=bool(rotate_with_owner),
                element=str(getattr(comp, 'element', '')),
                status=getattr(comp, 'status', None),
            )
            # VFX opcional: emitir partículas estilo 'slash' si el sistema existe
            try:
                from roguelike_game.ecs.components.particles.slash_emitter_component import SlashEmitterComponent
                # Elegir parámetros modestos; podrían ser extendidos leyendo cfg.vfx.particles
                count = 18
                life_frames = max(6, int(round(tper * 60)))
                size_min, size_max = 2, 6
                base_color = (255, 180, 120)
                speed_mult = 1.0
                world.components.setdefault('SlashEmitterComponent', {})[int(owner)] = SlashEmitterComponent(
                    radius=float(length),
                    arc_range=float(arc_rad),
                    count=int(count),
                    lifespan=int(life_frames),
                    size_range=(int(size_min), int(size_max)),
                    color=tuple(base_color),
                    speed_multiplier=float(speed_mult),
                    direction=(float(dir_x), float(dir_y)),
                    offset=float(offset),
                )
            except Exception:
                pass
