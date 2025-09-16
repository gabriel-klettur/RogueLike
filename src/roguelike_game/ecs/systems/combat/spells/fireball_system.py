import pygame
from roguelike_engine.utils.benchmark import benchmark
import math
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.explosion_component import ExplosionComponent
from roguelike_game.ecs.systems.combat.explosions_models import FireExplosionModel
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
import time
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker

class FireballSystem:
    """
    Sistema que actualiza fireballs: movimiento, edad, colisiones con NPC y tiles.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        # Actualizar cada fireball
        for eid in list(world.components.get('FireballComponent', {})):
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
                    world.remove_entity(eid)
                    continue
            # Evitar colisiones el primer frame para no impactar desde el spawn
            if comp.age == 1:
                continue
            # Expirar por lifespan
            if comp.age >= comp.lifespan:
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
            point = pygame.Rect(pos.x, pos.y, 1, 1)
            nearby = world.get_solid_tiles_for_rect(point)
            if nearby and point.collidelist(nearby) != -1:
                # Spawn ECS explosion at collision point
                x, y = pos.x, pos.y
                eid2 = world.create_entity()
                world.components['Position'][eid2] = Position(x, y)
                world.components['ExplosionComponent'][eid2] = ExplosionComponent(FireExplosionModel(x, y))
                world.remove_entity(eid)
                continue