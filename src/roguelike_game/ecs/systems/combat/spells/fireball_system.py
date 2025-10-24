import pygame
from roguelike_engine.utils.benchmark import benchmark
import math
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.explosion_component import ExplosionComponent
from roguelike_game.ecs.systems.combat.explosions_models import TimedEffectModel
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
import time
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker
import logging
logger = logging.getLogger(__name__)

class FireballSystem:
    """
    Sistema que actualiza fireballs: movimiento, edad, colisiones con NPC y tiles.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        # Cache de máscaras circulares por radio entero para pruebas rápidas de colisión máscara<->círculo
        self._circle_masks: dict[int, pygame.mask.Mask] = {}
    
    def update(self, world, camera=None):
        # Actualizar cada fireball
        fbd = world.components.get('FireballComponent', {})
        if not getattr(self, '_dbg_logged_count', False):
            setattr(self, '_dbg_logged_count', True)
            try:
                logger.debug("[FireballSystem] start update: fireballs=%d", len(fbd))
            except Exception:
                pass
        for eid in list(fbd):
            comp = world.components['FireballComponent'][eid]
            pos = world.components['Position'][eid]
            vel = world.components['Velocity'][eid]
            # Movimiento
            pos.x += vel.vx
            pos.y += vel.vy
            comp.age += 1
            # Radio efectivo de colisión (escala con meta)
            try:
                hit_radius = float(getattr(comp, 'hit_radius', 2.0))
            except Exception:
                hit_radius = 2.0
            # Trayectoria del frame para evitar tunneling
            prev_x = pos.x - vel.vx
            prev_y = pos.y - vel.vy
            dx = pos.x - prev_x
            dy = pos.y - prev_y
            dist = (dx*dx + dy*dy) ** 0.5
            step = max(2.0, float(hit_radius) * 0.5)
            samples = max(1, int(dist / step))
            if samples <= 1:
                sample_points = [(pos.x, pos.y)]
            else:
                sample_points = []
                for i in range(samples + 1):
                    t = i / samples
                    sx = prev_x + dx * t
                    sy = prev_y + dy * t
                    sample_points.append((sx, sy))
            # Destruir si supera rango configurado
            cfg = SPELLS.get(getattr(comp, 'spell_key', ''), {})
            max_range = cfg.get('range', 0)
            if max_range and comp.spawn_pos:
                dxr = pos.x - comp.spawn_pos[0]
                dyr = pos.y - comp.spawn_pos[1]
                if math.hypot(dxr, dyr) > max_range:
                    try:
                        logger.debug("[FireballSystem] remove eid=%s by range (%.1f > %.1f)", eid, math.hypot(dxr, dyr), max_range)
                    except Exception:
                        pass
                    world.remove_entity(eid)
                    continue
            # Evitar colisiones el primer frame para no impactar desde el spawn
            if comp.age == 1:
                continue
            # Expirar por lifespan
            if comp.age >= comp.lifespan:
                try:
                    logger.debug("[FireballSystem] remove eid=%s by lifespan age=%d lifespan=%d", eid, comp.age, comp.lifespan)
                except Exception:
                    pass
                world.remove_entity(eid)
                continue
            # Colisión con visuals activos de Spawner (Buildings): probar punto contra collision_tiles o collision_rect
            try:
                # Evitar colisiones con edificios ocultos o que no sean visuals de spawner
                hit_spawner_eid = None
                # sample_points precomputados arriba
                for b in getattr(world, 'buildings', []) or []:
                    try:
                        if getattr(b, 'runtime_hidden', False):
                            continue
                        if not bool(getattr(b, '_is_spawner_visual', False)):
                            continue
                        # Daño permitido sólo si el visual actual es damageable
                        eff = getattr(b, '_spawner_visual_life_cfg', None) or {}
                        if not bool(eff.get('damageable', False)):
                            continue
                        # Prueba principal: shape del asset (alpha mask de la imagen completa)
                        bm = getattr(b, 'model', None)
                        bmask = bm.get_full_mask() if bm is not None else None
                        if bmask is not None and getattr(bm, 'image', None) is not None:
                            iw, ih = bm.image.get_size()
                            hit = False
                            # Preparar máscara circular
                            r = max(1, int(round(hit_radius)))
                            cmask = self._circle_masks.get(r)
                            if cmask is None:
                                surf = pygame.Surface((2*r+1, 2*r+1), pygame.SRCALPHA)
                                pygame.draw.circle(surf, (255, 255, 255, 255), (r, r), r)
                                cmask = pygame.mask.from_surface(surf)
                                self._circle_masks[r] = cmask
                            for (sx, sy) in sample_points:
                                lx = int(round(sx - b.x))
                                ly = int(round(sy - b.y))
                                # Offset del círculo respecto a la máscara del edificio
                                offx = lx - r
                                offy = ly - r
                                if bmask.overlap(cmask, (offx, offy)) is not None:
                                    hit = True
                                    break
                            if not hit:
                                continue
                        else:
                            # Fallback: bounding rect + tiles
                            rect = getattr(b, 'rect', None) or b.collision_rect
                            # Aproximar círculo por un rectángulo envolvente y probar intersección
                            rect_hit = False
                            for (sx, sy) in sample_points:
                                circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                                if rect.colliderect(circle_rect):
                                    rect_hit = True
                                    break
                            if not rect_hit:
                                continue
                            tiles = list(getattr(b, 'collision_tiles', []) or [])
                            if tiles:
                                matched = False
                                for r in tiles:
                                    for (sx, sy) in sample_points:
                                        circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                                        if r.colliderect(circle_rect):
                                            matched = True
                                            break
                                    if matched:
                                        break
                                if not matched:
                                    continue
                        # Registrar impacto
                        se = getattr(b, '_spawner_eid', None)
                        if se is not None:
                            hit_spawner_eid = int(se)
                            break
                    except Exception:
                        continue
                if hit_spawner_eid is not None:
                    # Publicar evento de daño de spawner y generar explosión como feedback
                    sevts = world.components.setdefault('SpawnerDamageEvents', [])
                    sevts.append({'spawner_eid': int(hit_spawner_eid), 'damage': float(comp.damage), 'attacker': int(comp.caster) if comp.caster is not None else None})
                    # Spawn ECS explosion at collision point
                    try:
                        x, y = pos.x, pos.y
                        eid2 = world.create_entity()
                        world.components['Position'][eid2] = Position(x, y)
                        world.components['ExplosionComponent'][eid2] = ExplosionComponent(FireExplosionModel(x, y))
                    except Exception:
                        pass
                    world.remove_entity(eid)
                    continue
            except Exception:
                # No romper la lógica de fireball si hay fallo al procesar buildings
                pass
            # Colisión con NPCs (usar MaskCollider pixel-perfect siempre que exista)
            for target in world.get_entities_with('Position', 'MultiCollider', 'Health'):
                # Saltar self, caster y cadáveres con DeathTimer
                if target == eid or target == comp.caster:
                    continue
                if target in world.components.get('DeathTimer', {}) or target in world.components.get('DyingTag', {}):
                    continue
                multi = world.components['MultiCollider'][target]
                tpos = world.components['Position'][target]
                hit = False
                hit_pos = None
                hit_shape = None
                # Determinar si el target tiene al menos un MaskCollider
                has_mask = any(hasattr(c, 'mask') for c in multi.colliders.values())
                # 1) Intentar con máscaras (pixel-perfect) si existen (usar círculo y sweep por sample_points)
                if has_mask:
                    for col in multi.colliders.values():
                        if hasattr(col, 'mask'):
                            bx = tpos.x + col.offset_x
                            by = tpos.y + col.offset_y
                            # Preparar máscara circular
                            r = max(1, int(round(hit_radius)))
                            cmask = self._circle_masks.get(r)
                            if cmask is None:
                                surf = pygame.Surface((2*r+1, 2*r+1), pygame.SRCALPHA)
                                pygame.draw.circle(surf, (255, 255, 255, 255), (r, r), r)
                                cmask = pygame.mask.from_surface(surf)
                                self._circle_masks[r] = cmask
                            # Probar a lo largo de la trayectoria
                            for (sx, sy) in sample_points:
                                lx = int(round(sx - bx))
                                ly = int(round(sy - by))
                                offx = lx - r
                                offy = ly - r
                                if col.mask.overlap(cmask, (offx, offy)) is not None:
                                    hit = True
                                    hit_pos = (float(sx), float(sy))
                                    hit_shape = 'mask'
                                    break
                            if hit:
                                break
                # 2) Si no hubo hit con máscaras, probar círculos (feet) si existen
                if not hit:
                    for col in multi.colliders.values():
                        if hasattr(col, 'radius'):
                            cx = float(tpos.x + getattr(col, 'offset_x', 0))
                            cy = float(tpos.y + getattr(col, 'offset_y', 0))
                            cr = float(getattr(col, 'radius', 0))
                            if cr <= 0:
                                continue
                            # Probar a lo largo de la trayectoria
                            for (sx, sy) in sample_points:
                                dx2 = sx - cx
                                dy2 = sy - cy
                                # Colisión de dos círculos: distancia <= suma de radios
                                if (dx2*dx2 + dy2*dy2) <= (hit_radius + cr) * (hit_radius + cr):
                                    hit = True
                                    hit_pos = (float(sx), float(sy))
                                    hit_shape = 'circle'
                                    break
                            if hit:
                                break
                # 3) Solo si NO hay máscaras ni círculos o no hubo hit, usar fallback a rectángulos
                if not hit and not has_mask:
                    for col in multi.colliders.values():
                        if hasattr(col, 'mask'):
                            continue
                        rect = pygame.Rect(
                            tpos.x + col.offset_x,
                            tpos.y + col.offset_y,
                            getattr(col, 'width', 0),
                            getattr(col, 'height', 0)
                        )
                        for (sx, sy) in sample_points:
                            circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                            if rect.colliderect(circle_rect):
                                hit = True
                                hit_pos = (float(sx), float(sy))
                                hit_shape = 'rect'
                                break
                        if hit:
                            break

                if hit:
                    # Spawn preset-based explosion VFX at impact point (only if preset explicitly configured)
                    try:
                        preset_id = None
                        ttl_ticks = None
                        if cfg is None:
                            cfg = SPELLS.get(getattr(comp, 'spell_key', ''), {})
                        vfx_obj = None
                        try:
                            vfx_attr = getattr(cfg, 'vfx', None)
                            if isinstance(vfx_attr, dict):
                                vfx_obj = vfx_attr
                            else:
                                vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                        except Exception:
                            vfx_obj = None
                        if isinstance(vfx_obj, dict):
                            impact = vfx_obj.get('impact') or {}
                            if isinstance(impact, dict):
                                if isinstance(impact.get('preset'), str):
                                    preset_id = impact.get('preset')
                                if isinstance(impact.get('ttl'), (int, float)):
                                    ttl_ticks = int(impact.get('ttl'))
                                exp = impact.get('explosion') or {}
                                if isinstance(exp, dict):
                                    if isinstance(exp.get('preset'), str):
                                        preset_id = exp.get('preset')
                                    if isinstance(exp.get('ttl'), (int, float)):
                                        ttl_ticks = int(exp.get('ttl'))
                        if isinstance(preset_id, str) and preset_id:
                            x, y = hit_pos if hit_pos else (pos.x, pos.y)
                            peid = world.create_entity()
                            world.components.setdefault('Position', {})[peid] = Position(x, y)
                            # Escalar impacto según multiplicador del proyectil
                            try:
                                smul = float(getattr(comp, 'vfx_scale_multiplier', 1.0))
                            except Exception:
                                smul = 1.0
                            world.components.setdefault('ParticlePresetComponent', {})[peid] = ParticlePresetComponent(preset_id, scale_multiplier=smul)
                            world.components.setdefault('ExplosionComponent', {})[peid] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                    except Exception:
                        pass
                    # Inmortalidad del jugador en godmode
                    is_player = target in world.components.get('PlayerTagComponent', {})
                    godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player
                    # One-shot si el caster es jugador y godmode activo
                    gm_attacker = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (comp.caster in world.components.get('PlayerTagComponent', {}))
                    if not godmode:
                        hp = world.components['Health'][target]
                        if gm_attacker:
                            hp.current_hp = 0
                        else:
                            hp.current_hp = max(0, hp.current_hp - comp.damage)
                    # Registrar último atacante para atribuir KO si entra en UnconsciousState (solo si aplica daño)
                    if not godmode:
                        world.components.setdefault('LastAttacker', {})[target] = LastAttacker(comp.caster, time.time())
                    # Push debug event for outline persistence (consumed by SpellCollisionDebugSystem)
                    dbg = world.components.setdefault('DebugSpellHits', {})
                    queue = dbg.setdefault('_queue', [])
                    queue.append({'type': 'FB', 'src': eid, 'target': target, 'pos': hit_pos, 'shape': hit_shape})
                    world.remove_entity(eid)
                    # Publicar eventos FSM para NPCs golpeados por jugador o jugador golpeado por NPC
                    caster = comp.caster
                    if caster in world.components.get('PlayerTagComponent', {}):
                        # Jugador -> NPC
                        attacker_pos = world.components['Position'][caster]
                        defender_pos = world.components['Position'][target]
                        from_left = attacker_pos.x < defender_pos.x
                        qmap = world.components.setdefault('FSMEventQueue', {})
                        q = qmap.setdefault(target, [])
                        q.append({"type": "OnHit", "from_left": from_left})
                        if not godmode:
                            hp = world.components['Health'][target]
                            if hp.current_hp <= 0:
                                pass
                            # Evento de COMBO
                            combo_q = world.components.setdefault('ComboEventQueue', [])
                            combo_q.append({
                                'attacker': caster,
                                'target': target,
                                'damage': float(comp.damage),
                                'source': 'fireball',
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

                    elif is_player:
                        # NPC -> Jugador (omitir efectos de daño en godmode)
                        if not godmode:
                            attacker_pos = world.components['Position'].get(caster)
                            defender_pos = world.components['Position'].get(target)
                            if attacker_pos and defender_pos:
                                from_left = attacker_pos.x < defender_pos.x
                            else:
                                from_left = False
                            qmap = world.components.setdefault('FSMEventQueue', {})
                            q = qmap.setdefault(target, [])
                            q.append({"type": "OnHit", "from_left": from_left})
                            hp = world.components['Health'][target]
                            if hp.current_hp <= 0:
                                pass
                            # Romper combo del jugador al recibir daño
                            combo_q = world.components.setdefault('ComboEventQueue', [])

                            combo_q.append({'type': 'break', 'entity': target})
                    break

            # Colisión con tiles sólidos
            # Colisiones contra tiles a lo largo de la trayectoria
            collided = False
            for (sx, sy) in sample_points:
                px = int(round(sx))
                py = int(round(sy))
                circle_rect = pygame.Rect(int(px - hit_radius), int(py - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                nearby = world.get_solid_tiles_for_rect(circle_rect)
                if nearby and any(r.colliderect(circle_rect) for r in nearby):
                    collided = True
                    break
            if collided:
                # Spawn preset-based explosion at collision point (only if preset explicitly configured)
                try:
                    preset_id = None
                    ttl_ticks = None
                    vfx_obj = None
                    try:
                        vfx_attr = getattr(cfg, 'vfx', None)
                        if isinstance(vfx_attr, dict):
                            vfx_obj = vfx_attr
                        else:
                            vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                    except Exception:
                        vfx_obj = None
                    if isinstance(vfx_obj, dict):
                        impact = vfx_obj.get('impact') or {}
                        if isinstance(impact, dict):
                            if isinstance(impact.get('preset'), str):
                                preset_id = impact.get('preset')
                            if isinstance(impact.get('ttl'), (int, float)):
                                ttl_ticks = int(impact.get('ttl'))
                            exp = impact.get('explosion') or {}
                            if isinstance(exp, dict):
                                if isinstance(exp.get('preset'), str):
                                    preset_id = exp.get('preset')
                                if isinstance(exp.get('ttl'), (int, float)):
                                    ttl_ticks = int(exp.get('ttl'))
                    if isinstance(preset_id, str) and preset_id:
                        x, y = float(px), float(py)
                        eid2 = world.create_entity()
                        world.components.setdefault('Position', {})[eid2] = Position(x, y)
                        # Escalar impacto según multiplicador del proyectil
                        try:
                            smul = float(getattr(comp, 'vfx_scale_multiplier', 1.0))
                        except Exception:
                            smul = 1.0
                        world.components.setdefault('ParticlePresetComponent', {})[eid2] = ParticlePresetComponent(preset_id, scale_multiplier=smul)
                        world.components.setdefault('ExplosionComponent', {})[eid2] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                except Exception:
                    pass
                world.remove_entity(eid)
                continue