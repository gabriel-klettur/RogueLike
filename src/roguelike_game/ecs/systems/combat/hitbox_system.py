import math
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_game.ecs.systems.fsm.states.damage_state import DamageState
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.ecs.systems.fsm.states.monster.alert_chase_state import AlertChaseState
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy

class HitboxSystem:
    """
    ECS system that processes HitboxComponent: decrements lifespan,
    detects collisions within an arc, applies damage once per target.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.7. HitboxSystem.update")
    def update(self, world, camera=None):
        positions = world.components.get('Position', {})
        healths = world.components.get('Health', {})
        hitboxes = world.components.get('HitboxComponent', {})
        for eid, hb in list(hitboxes.items()):
            pos = positions.get(eid)
            if pos is None:
                world.remove_entity(eid)
                continue
            # decrement lifespan
            hb.lifespan -= 1
            if hb.lifespan < 0:
                world.remove_entity(eid)
                continue
            # pixel-perfect collision using masks
            cx, cy = pos.x, pos.y
            dir_x, dir_y = hb.direction
            dir_ang = math.atan2(dir_y, dir_x)
            r = hb.radius
            # build hitbox mask as filled sector
            left, top = cx - r, cy - r
            w, h = int(r*2), int(r*2)
            screen_left, screen_top = camera.apply((left, top))
            surf = pygame.Surface((w, h), pygame.SRCALPHA)
            start_ang = dir_ang - hb.arc_angle/2
            end_ang = dir_ang + hb.arc_angle/2
            pts = [(r, r)]
            segs = max(4, int(hb.arc_angle/(2*math.pi)*16))
            for i in range(segs+1):
                ang = start_ang + (end_ang - start_ang)*i/segs
                pts.append((r + math.cos(ang)*r, r + math.sin(ang)*r))
            pygame.draw.polygon(surf, (255,255,255), pts)
            hitmask = pygame.mask.from_surface(surf)
            r2 = r*r
            multi_map = world.components.get('MultiCollider', {})
            for target in list(healths.keys()):
                if target == hb.owner or target in hb.hit_targets:
                    continue
                tpos = positions.get(target)
                if tpos is None:
                    continue
                hit_any = False
                comp = multi_map.get(target)
                if comp:
                    for collider in comp.colliders.values():
                        rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                        sx, sy = camera.apply((rect_w.x, rect_w.y))
                        off = (int(sx - screen_left), int(sy - screen_top))
                        if hasattr(collider, 'mask'):
                            target_mask = collider.mask
                        else:
                            tmp = pygame.Surface((rect_w.width, rect_w.height))
                            tmp.fill((255,255,255))
                            target_mask = pygame.mask.from_surface(tmp)
                        if hitmask.overlap(target_mask, off):
                            hit_any = True
                            break
                    if not hit_any:
                        continue
                else:
                    # fallback center-point
                    dx, dy = tpos.x - cx, tpos.y - cy
                    if dx*dx + dy*dy > r2:
                        continue
                    ang = math.atan2(dy, dx)
                    diff = abs((ang - dir_ang + math.pi) % (2*math.pi) - math.pi)
                    if diff <= hb.arc_angle/2:
                        hit_any = True
                    else:
                        continue
                identity = world.components.get('Identity', {}).get(target)
                name = identity.name if identity else 'Unknown'
                print(f"[DEBUG][HitboxSystem] Hit! target {target} ({name}), hp_before={healths[target].current_hp}, damage={hb.damage}")
                # apply damage
                health = healths[target]
                health.current_hp = max(0, health.current_hp - hb.damage)
                hb.hit_targets.add(target)
                if hb.owner in world.components.get('PlayerTagComponent', {}):
                    attacker_pos = world.components['Position'][hb.owner]
                    defender_pos = world.components['Position'][target]
                    from_left = attacker_pos.x < defender_pos.x
                    fsm = world.components['NPCState'][target].fsm
                    proxy = _EntityProxy(world, target)
                    current = fsm.current_state
                    next_state = AttackState() if isinstance(current, AttackState) else AlertChaseState()
                    fsm.change_state(DamageState(next_state, from_left), proxy)
                    if health.current_hp <= 0:
                        fsm.change_state(DeathState(), proxy)