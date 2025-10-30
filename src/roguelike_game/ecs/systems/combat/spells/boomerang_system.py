import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.utils.health_utils import is_neutral


class BoomerangSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'BoomerangSystem.update')
    def update(self, world, camera=None):
        comps = world.components.get('BoomerangComponent', {})
        pos_map = world.components.get('Position', {})
        vel_map = world.components.get('Velocity', {})
        hp_map = world.components.get('Health', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})

        for eid, comp in list(comps.items()):
            pos = pos_map.get(eid)
            vel = vel_map.get(eid)
            if pos is None or vel is None:
                comps.pop(eid, None)
                continue
            prev_x, prev_y = float(pos.x), float(pos.y)
            pos.x += vel.vx
            pos.y += vel.vy
            comp.age += 1
            comp.distance += math.hypot(vel.vx, vel.vy)

            if comp.state == 'outbound' and comp.range > 0 and comp.distance >= comp.range:
                comp.state = 'returning'
            if comp.state == 'returning':
                cx = cy = 0.0
                caster_pos = world.components.get('Position', {}).get(comp.caster)
                if caster_pos is not None:
                    cx, cy = float(caster_pos.x), float(caster_pos.y)
                    spr = world.components.get('Sprite', {}).get(comp.caster)
                    if spr and getattr(spr, 'image', None) is not None:
                        w, h = spr.image.get_size()
                        cx += w/2
                        cy += h/2
                else:
                    cx, cy = comp.spawn_pos
                tx, ty = cx - pos.x, cy - pos.y
                l = math.hypot(tx, ty) or 1.0
                speed = comp.return_speed if comp.return_speed > 0 else comp.speed
                vel.vx = (tx / l) * speed
                vel.vy = (ty / l) * speed
                if tx*tx + ty*ty <= comp.hit_radius * comp.hit_radius:
                    # cleanup visuals
                    world.remove_entity(eid)
                    continue

            # collisions vs entities
            for target, thp in list(hp_map.items()):
                if target == comp.caster or target in dead_map or target in dying_map:
                    continue
                if is_neutral(world, target):
                    continue
                tpos = world.components.get('Position', {}).get(target)
                if tpos is None:
                    continue
                tcx, tcy = float(tpos.x), float(tpos.y)
                try:
                    mc = world.components.get('MultiCollider', {}).get(target)
                    if mc is not None:
                        cols = getattr(mc, 'colliders', {}) or {}
                        feet = cols.get('feet') if isinstance(cols, dict) else None
                        if feet is not None:
                            tcx += float(getattr(feet, 'offset_x', 0.0))
                            tcy += float(getattr(feet, 'offset_y', 0.0))
                    else:
                        col = world.components.get('Collider', {}).get(target)
                        if col is not None:
                            tcx += float(getattr(col, 'offset_x', 0.0))
                            tcy += float(getattr(col, 'offset_y', 0.0))
                except Exception:
                    pass
                dx = tcx - pos.x
                dy = tcy - pos.y
                entity_radius = 0.0
                try:
                    mc2 = world.components.get('MultiCollider', {}).get(target)
                    if mc2 is not None:
                        cols2 = getattr(mc2, 'colliders', {}) or {}
                        feet2 = cols2.get('feet') if isinstance(cols2, dict) else None
                        if feet2 is not None and hasattr(feet2, 'radius'):
                            entity_radius = max(entity_radius, float(getattr(feet2, 'radius', 0.0)))
                except Exception:
                    pass
                try:
                    col2 = world.components.get('Collider', {}).get(target)
                    if col2 is not None:
                        candidate = 0.5 * max(float(getattr(col2, 'width', 0)), float(getattr(col2, 'height', 0)))
                        entity_radius = max(entity_radius, candidate)
                except Exception:
                    pass
                if (dx*dx + dy*dy) <= (comp.hit_radius + entity_radius) * (comp.hit_radius + entity_radius) + 1e-6:
                    if target in comp.hit_targets:
                        continue
                    comp.hit_targets.add(target)
                    if not is_neutral(world, target):
                        thp.current_hp = max(0, thp.current_hp - int(comp.damage))
                    if not comp.passes_through and comp.state == 'outbound':
                        comp.state = 'returning'
                        # reverse velocity immediately
                        vel.vx = -vel.vx
                        vel.vy = -vel.vy
                        break
