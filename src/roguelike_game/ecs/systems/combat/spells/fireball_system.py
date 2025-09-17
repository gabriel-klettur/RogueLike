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
            # Colisión con NPCs (usar MaskCollider pixel-perfect siempre que exista)
            for target in world.get_entities_with('Position', 'MultiCollider', 'Health'):
                # Saltar self, caster y cadáveres con DeathTimer
                if target == eid or target == comp.caster:
                    continue
                if target in world.components.get('DeathTimer', {}):
                    continue
                multi = world.components['MultiCollider'][target]
                tpos = world.components['Position'][target]
                hit = False
                hit_pos = None
                hit_shape = None
                # Determinar si el target tiene al menos un MaskCollider
                has_mask = any(isinstance(c, MaskCollider) for c in multi.colliders.values())
                # 1) Intentar con máscaras (pixel-perfect) si existen
                if has_mask:
                    for col in multi.colliders.values():
                        if isinstance(col, MaskCollider):
                            bx = tpos.x + col.offset_x
                            by = tpos.y + col.offset_y
                            lx = int(pos.x - bx)
                            ly = int(pos.y - by)
                            mw, mh = col.mask.get_size()
                            if 0 <= lx < mw and 0 <= ly < mh and col.mask.get_at((lx, ly)):
                                hit = True
                                hit_pos = (float(pos.x), float(pos.y))
                                hit_shape = 'mask'
                                break
                # 2) Solo si NO hay máscaras, usar fallback a rectángulos
                if not hit and not has_mask:
                    for col in multi.colliders.values():
                        if not isinstance(col, MaskCollider):
                            rect = pygame.Rect(
                                tpos.x + col.offset_x,
                                tpos.y + col.offset_y,
                                getattr(col, 'width', 0),
                                getattr(col, 'height', 0)
                            )
                            if rect.collidepoint(pos.x, pos.y):
                                hit = True
                                hit_pos = (float(pos.x), float(pos.y))
                                hit_shape = 'rect'
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
                            world.components.setdefault('ParticlePresetComponent', {})[peid] = ParticlePresetComponent(preset_id)
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
                                q.append({"type": "OnDeath"})
                                # Evento de kill para combo basado en muertes
                                combo_q = world.components.setdefault('ComboEventQueue', [])
                                combo_q.append({'type': 'kill', 'entity': caster, 'target': target})
                                world.components.setdefault('ComboKillCounted', set()).add(target)
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
                                q.append({"type": "OnDeath"})
                            # Romper combo del jugador al recibir daño
                            combo_q = world.components.setdefault('ComboEventQueue', [])
                            combo_q.append({'type': 'break', 'entity': target})
                    break

            # Colisión con tiles sólidos
            px = int(round(pos.x))
            py = int(round(pos.y))
            point = pygame.Rect(px - 1, py - 1, 3, 3)
            nearby = world.get_solid_tiles_for_rect(point)
            if nearby and point.collidelist(nearby) != -1:
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
                        world.components.setdefault('ParticlePresetComponent', {})[eid2] = ParticlePresetComponent(preset_id)
                        world.components.setdefault('ExplosionComponent', {})[eid2] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                except Exception:
                    pass
                world.remove_entity(eid)
                continue