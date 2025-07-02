# Path: src/roguelike_game/ecs/systems/combat/spells/fireball_system.py
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
from roguelike_game.ecs.systems.fsm.states.damage_state import DamageState
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
import math
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.systems.fsm.states.monster.alert_chase_state import AlertChaseState
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.explosion_component import ExplosionComponent
from roguelike_game.ecs.systems.combat.explosions_models import FireExplosionModel

class FireballSystem:
    """
    Sistema que actualiza fireballs: movimiento, edad, colisiones con NPC y tiles.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.FireballSystem.update")
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
            # Colisión con NPCs
            for target in world.get_entities_with('Position', 'MultiCollider', 'Health'):
                # Saltar self, caster y cadáveres con DeathTimer
                if target == eid or target == comp.caster:
                    continue
                if target in world.components.get('DeathTimer', {}):
                    continue
                multi = world.components['MultiCollider'][target]
                body = multi.colliders.get('body')
                if body:
                    tpos = world.components['Position'][target]
                    # Usar tamaño de mask si existe, sino width/height
                    if hasattr(body, 'mask'):
                        w, h = body.mask.get_size()
                    else:
                        w, h = body.width, body.height
                    rect = pygame.Rect(tpos.x + body.offset_x,
                                       tpos.y + body.offset_y,
                                       w, h)
                    if rect.collidepoint(pos.x, pos.y):
                        hp = world.components['Health'][target]
                        hp.current_hp = max(0, hp.current_hp - comp.damage)
                        world.remove_entity(eid)
                        # Si el daño viene de fireball de jugador, estado alerta de chase
                        caster = comp.caster
                        if caster in world.components.get('PlayerTagComponent', {}):
                            fsm = world.components['NPCState'][target].fsm                            
                            # determinar dirección de daño y siguiente estado
                            attacker_pos = world.components['Position'][caster]
                            defender_pos = world.components['Position'][target]
                            from_left = attacker_pos.x < defender_pos.x
                            proxy = _EntityProxy(world, target)
                            # Si ya estaba en AttackState, volver a Attack; sino AlertChase
                            current = fsm.current_state
                            next_state = AttackState() if isinstance(current, AttackState) else AlertChaseState()
                            fsm.change_state(DamageState(next_state, from_left), proxy)
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