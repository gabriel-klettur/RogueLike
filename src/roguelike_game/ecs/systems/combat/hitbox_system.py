import math
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
import time
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker

import logging
logger = logging.getLogger(__name__)

class HitboxSystem:
    """
    ECS system that processes HitboxComponent: decrements lifespan,
    detects collisions within an arc, applies damage once per target.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
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

            # --- Buildings hit detection: generate BuildingDamageEvents ---
            try:
                arc_world_rect = pygame.Rect(int(left), int(top), int(w), int(h))
                hit_buildings = set()
                for b in getattr(world, 'buildings', []) or []:
                    # Skip spawner visuals and non-solid check is not required for damage, but keep visuals optionally damageable
                    if getattr(b, '_is_spawner_visual', False):
                        continue
                    # Quick reject by bounding box
                    try:
                        if not arc_world_rect.colliderect(b.collision_rect):
                            continue
                    except Exception:
                        continue
                    # Test per collision tile for precise overlap
                    try:
                        for rect_w in b.collision_tiles:
                            if not arc_world_rect.colliderect(rect_w):
                                continue
                            sx, sy = camera.apply((rect_w.x, rect_w.y))
                            off = (int(sx - screen_left), int(sy - screen_top))
                            # Build a rectangular mask for the tile
                            tmp = pygame.Surface((rect_w.width, rect_w.height))
                            tmp.fill((255,255,255))
                            target_mask = pygame.mask.from_surface(tmp)
                            if hitmask.overlap(target_mask, off):
                                # Identify building by spawn_id if present, else by id
                                bid = getattr(b, 'spawn_id', None) or getattr(b, 'id', None)
                                if bid is not None:
                                    hit_buildings.add(bid)
                                break
                        # Early-out if already registered a hit for this building
                    except Exception:
                        continue
                if hit_buildings:
                    evts = world.components.setdefault('BuildingDamageEvents', [])
                    for bid in hit_buildings:
                        evts.append({'building_key': str(bid), 'damage': hb.damage})
            except Exception:
                # Never break combat on building processing issues
                pass
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
                logger.debug(f"[DEBUG][HitboxSystem] Hit! target {target} ({name}), hp_before={healths[target].current_hp}, damage={hb.damage}")
                # apply damage
                health = healths[target]
                health.current_hp = max(0, health.current_hp - hb.damage)
                # record last attacker for KO attribution
                world.components.setdefault('LastAttacker', {})[target] = LastAttacker(hb.owner, time.time())
                hb.hit_targets.add(target)
                if hb.owner in world.components.get('PlayerTagComponent', {}):
                    attacker_pos = world.components['Position'][hb.owner]
                    defender_pos = world.components['Position'][target]
                    from_left = attacker_pos.x < defender_pos.x
                    qmap = world.components.setdefault('FSMEventQueue', {})
                    q = qmap.setdefault(target, [])
                    q.append({"type": "OnHit", "from_left": from_left})
                    if health.current_hp <= 0:
                        q.append({"type": "OnDeath"})
                        # Evento de kill para combo basado en muertes
                        combo_q = world.components.setdefault('ComboEventQueue', [])
                        combo_q.append({'type': 'kill', 'entity': hb.owner, 'target': target})
                    combo_q = world.components.setdefault('ComboEventQueue', [])
                    combo_q.append({
                        'attacker': hb.owner,
                        'target': target,
                        'damage': float(hb.damage),
                        'source': 'hitbox',
                        'time': float(time.time()),
                    })
                elif target in world.components.get('PlayerTagComponent', {}):
                    # NPC or other entity hit the player -> publish OnHit/OnDeath for player
                    attacker_pos = positions.get(hb.owner)
                    defender_pos = positions.get(target)
                    if attacker_pos and defender_pos:
                        from_left = attacker_pos.x < defender_pos.x
                    else:
                        from_left = False
                    qmap = world.components.setdefault('FSMEventQueue', {})
                    q = qmap.setdefault(target, [])
                    q.append({"type": "OnHit", "from_left": from_left})
                    if health.current_hp <= 0:
                        q.append({"type": "OnDeath"})
                    # Break player's combo upon taking damage
                    combo_q = world.components.setdefault('ComboEventQueue', [])
                    combo_q.append({'type': 'break', 'entity': target})