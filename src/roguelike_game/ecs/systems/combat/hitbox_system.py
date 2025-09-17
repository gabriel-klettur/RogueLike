import math
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
import time
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker
from roguelike_game.ecs.utils.position_utils import compute_entity_center

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

            # --- Buildings hit detection: generate BuildingDamageEvents and SpawnerDamageEvents ---
            try:
                arc_world_rect = pygame.Rect(int(left), int(top), int(w), int(h))
                hit_buildings = set()
                hit_spawners = set()
                for b in getattr(world, 'buildings', []) or []:
                    is_spawner_visual = bool(getattr(b, '_is_spawner_visual', False))
                    # Quick skip if hidden at runtime (editor can still show)
                    try:
                        if getattr(b, 'runtime_hidden', False):
                            continue
                    except Exception:
                        pass
                    # Quick reject by bounding box (use full rect for spawner visuals to include top part)
                    try:
                        quick_rect = getattr(b, 'rect', None) if is_spawner_visual else b.collision_rect
                        if not arc_world_rect.colliderect(quick_rect):
                            continue
                    except Exception:
                        continue
                    # Test hit against building shape
                    try:
                        if is_spawner_visual:
                            # Prefer full-image alpha mask for visual shape
                            eff = getattr(b, '_spawner_visual_life_cfg', None) or {}
                            damageable = bool(eff.get('damageable', False))
                            if not damageable:
                                continue
                            try:
                                bm = getattr(b, 'model', None)
                                bmask = bm.get_full_mask() if bm is not None else None
                            except Exception:
                                bmask = None
                            if bmask is not None:
                                # Offset from arc hitmask origin (screen_left, screen_top) to building top-left in screen coords
                                bx, by = camera.apply((b.x, b.y))
                                off = (int(bx - screen_left), int(by - screen_top))
                                if hitmask.overlap(bmask, off):
                                    se = getattr(b, '_spawner_eid', None)
                                    if se is not None:
                                        hit_spawners.add(int(se))
                                    continue
                            # Fallback: per-tile rectangles if mask missing
                            for rect_w in b.collision_tiles:
                                if not arc_world_rect.colliderect(rect_w):
                                    continue
                                sx, sy = camera.apply((rect_w.x, rect_w.y))
                                off = (int(sx - screen_left), int(sy - screen_top))
                                tmp = pygame.Surface((rect_w.width, rect_w.height))
                                tmp.fill((255,255,255))
                                target_mask = pygame.mask.from_surface(tmp)
                                if hitmask.overlap(target_mask, off):
                                    se = getattr(b, '_spawner_eid', None)
                                    if se is not None:
                                        hit_spawners.add(int(se))
                                    break
                        else:
                            # Non-spawner buildings: keep per-tile rectangle checks
                            for rect_w in b.collision_tiles:
                                if not arc_world_rect.colliderect(rect_w):
                                    continue
                                sx, sy = camera.apply((rect_w.x, rect_w.y))
                                off = (int(sx - screen_left), int(sy - screen_top))
                                tmp = pygame.Surface((rect_w.width, rect_w.height))
                                tmp.fill((255,255,255))
                                target_mask = pygame.mask.from_surface(tmp)
                                if hitmask.overlap(target_mask, off):
                                    bid = getattr(b, 'spawn_id', None) or getattr(b, 'id', None)
                                    if bid is not None:
                                        hit_buildings.add(bid)
                                    break
                    except Exception:
                        continue
                if hit_buildings:
                    evts = world.components.setdefault('BuildingDamageEvents', [])
                    for bid in hit_buildings:
                        evts.append({'building_key': str(bid), 'damage': hb.damage})
                if hit_spawners:
                    sevts = world.components.setdefault('SpawnerDamageEvents', [])
                    for sp_eid in hit_spawners:
                        sevts.append({'spawner_eid': int(sp_eid), 'damage': hb.damage, 'attacker': int(hb.owner)})
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
                # apply damage (omit if player in godmode)
                is_player_target = target in world.components.get('PlayerTagComponent', {})
                godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player_target
                health = healths[target]
                # One-shot si atacante es jugador y godmode activo
                gm_attacker = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (hb.owner in world.components.get('PlayerTagComponent', {}))
                if not godmode:
                    if gm_attacker:
                        health.current_hp = 0
                    else:
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
                    # Actualizar HUD de objetivo (centrado arriba)
                    try:
                        hud = world.components.setdefault('TargetHUD', {})
                        hud['target_eid'] = int(target)
                        hud['last_hit_time'] = float(time.time())
                        hud.setdefault('ttl_s', 3.0)
                    except Exception:
                        pass
                elif target in world.components.get('PlayerTagComponent', {}) and not godmode:
                    # NPC or other entity hit the player -> publish OnHit/OnDeath for player
                    attacker_pos = positions.get(hb.owner)
                    defender_pos = positions.get(target)
                    # Compute centers when possible for consistent facing/origin
                    try:
                        spr_map = world.components.get('Sprite', {})
                        scl_map = world.components.get('Scale', {})
                        if attacker_pos:
                            aspr = spr_map.get(hb.owner)
                            ascl = scl_map.get(hb.owner)
                            if aspr:
                                ac = compute_entity_center(attacker_pos, aspr, ascl)
                                ax = float(ac.x)
                            else:
                                ax = float(attacker_pos.x)
                        else:
                            ax = float(cx)
                        if defender_pos:
                            dspr = spr_map.get(target)
                            dscl = scl_map.get(target)
                            if dspr:
                                dc = compute_entity_center(defender_pos, dspr, dscl)
                                dx_center = float(dc.x)
                            else:
                                dx_center = float(defender_pos.x)
                        else:
                            dx_center = float(cx)
                        from_left = ax < dx_center
                    except Exception:
                        from_left = bool(attacker_pos and defender_pos and (attacker_pos.x < defender_pos.x))
                    qmap = world.components.setdefault('FSMEventQueue', {})
                    q = qmap.setdefault(target, [])
                    q.append({"type": "OnHit", "from_left": from_left})
                    if health.current_hp <= 0:
                        q.append({"type": "OnDeath"})
                    # Break player's combo upon taking damage
                    combo_q = world.components.setdefault('ComboEventQueue', [])
                    combo_q.append({'type': 'break', 'entity': target})
                    # Publish debug event to visualize NPC hitbox hit on the player
                    try:
                        dbg = world.components.setdefault('DebugAttackEvents', {})
                        dq = dbg.setdefault('_queue', [])
                        player_pos = defender_pos
                        # Use hitbox center as origin to reflect actual attack origin
                        src_x = cx
                        src_y = cy
                        if player_pos:
                            dq.append({
                                'type': 'NPC_HITBOX_HIT',
                                'attacker': int(hb.owner),
                                'target': int(target),
                                'posA': (float(src_x), float(src_y)),
                                'posB': (float(player_pos.x), float(player_pos.y)),
                                'hb_center': (float(cx), float(cy)),
                                'hb_radius': float(r),
                                'arc_angle': float(hb.arc_angle),
                                'direction': (float(hb.direction[0]), float(hb.direction[1])),
                                'damage': float(hb.damage),
                                'time': float(time.time()),
                            })
                    except Exception:
                        pass