"""
Module: input_system.py
Sistema que traduce el estado de teclado a InputComponent y actualiza Velocity.
"""
import pygame
import time
import math

from roguelike_game.ecs.components.combat.attack_cooldown import AttackCooldown
from roguelike_game.ecs.components.ai.wants_to_melee import WantsToMelee
from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.fsm.states.idle_state import IdleState
from roguelike_game.ecs.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.fsm.states.player.player_attack_state import PlayerAttackState
from roguelike_game.ecs.fsm.states.player.player_spell_select_state import PlayerSpellSelectState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy

class InputSystem:
    """
    Captura el estado del teclado y actualiza InputComponent y Velocity.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        # Mapear estado previo de click y right-click para detección de flanco ascendente
        self.prev_click = {}
        self.prev_right = {}

    @benchmark(lambda self: self.perf_log, "4.2.2.InputSystem.update")
    def update(self, world, camera=None):
        # Obtener estado actual del teclado
        keys = pygame.key.get_pressed()
        # Para cada entidad con InputComponent
        for eid, inp in world.components.get('InputComponent', {}).items():
            # Movimiento en ejes X e Y
            inp.move_x = int(keys[pygame.K_RIGHT]) - int(keys[pygame.K_LEFT])
            inp.move_y = int(keys[pygame.K_DOWN]) - int(keys[pygame.K_UP])
            #print(f"[DEBUG][{time.time():.3f}] eid={eid} move=({inp.move_x},{inp.move_y}), click={inp.click}")
            # Actualizar velocidad según MovementSpeed
            vel = world.components.get('Velocity', {}).get(eid)
            ms = world.components.get('MovementSpeed', {}).get(eid)
            if vel and ms:
                dx, dy = inp.move_x, inp.move_y
                speed = ms.speed
                length = math.hypot(dx, dy)
                if length > 0:
                    vel.vx = dx / length * speed
                    vel.vy = dy / length * speed
                else:
                    vel.vx = vel.vy = 0

            # Integración FSM para Player
            # Detectar Player usando componente PlayerTagComponent
            if eid in world.components.get('PlayerTagComponent', {}):
                state_comp = world.components.get('NPCState', {}).get(eid)
                if state_comp:
                    current = state_comp.fsm.current_state
                    proxy = _EntityProxy(world, eid)
                    # Movimiento
                    if (inp.move_x != 0 or inp.move_y != 0) and isinstance(current, IdleState):
                        state_comp.fsm.change_state(MoveState(), proxy)
                    elif inp.move_x == 0 and inp.move_y == 0 and isinstance(current, MoveState):
                        state_comp.fsm.change_state(IdleState(), proxy)
                    # Ataque físico
                    if inp.attack:
                        state_comp.fsm.change_state(PlayerAttackState(), proxy)
                    # Hechizos (q/e)
                    if inp.skill_q or inp.skill_e:
                        state_comp.fsm.change_state(PlayerSpellSelectState(), proxy)

            # Mapear habilidades Q, E, X
            inp.skill_q = bool(keys[pygame.K_q])
            inp.skill_e = bool(keys[pygame.K_e])
            inp.skill_x = bool(keys[pygame.K_x])
            # Habilidades 1 y 2: darkball e iceball
            inp.skill_1 = bool(keys[pygame.K_1])
            inp.skill_2 = bool(keys[pygame.K_2])
            # Resetear flags de Q/E/X tras lectura para evitar duplicados
            if inp.skill_x:
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='healing_aura')
                inp.skill_x = False
            if inp.skill_e:
                print(f"[DEBUG][{time.time():.3f}] eid={eid} skill_e -> slash")
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='slash')
                inp.skill_e = False
            if inp.skill_q:
                print(f"[DEBUG][{time.time():.3f}] eid={eid} skill_q -> lightball")
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='lightball')
                inp.skill_q = False
            if inp.skill_1:
                print(f"[DEBUG][{time.time():.3f}] eid={eid} skill_1 -> darkball")
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='darkball')
                inp.skill_1 = False
            if inp.skill_2:
                print(f"[DEBUG][{time.time():.3f}] eid={eid} skill_2 -> iceball")
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='iceball')
                inp.skill_2 = False
            # Actualizar estado del click y detectar flanco ascendente
            curr_click = bool(pygame.mouse.get_pressed()[0])
            inp.click = curr_click
            prev = self.prev_click.get(eid, False)
            # Generar intención de fireball sólo en flanco ascendente
            if eid in world.components.get('PlayerTagComponent', {}) and curr_click and not prev:
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='fireball')
            # Guardar estado click para próxima iteración
            self.prev_click[eid] = curr_click
            # Lanzar el beam con click del medio
            middle = pygame.mouse.get_pressed()[1]
            if middle:
                print(f"[DEBUG][{time.time():.3f}] eid={eid} middle-click -> laser_beam")
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='laser_beam')
            # Detectar dash (right-click) por flanco ascendente
            curr_right = bool(pygame.mouse.get_pressed()[2])
            prev_r = self.prev_right.get(eid, False)
            if curr_right and not prev_r:
                print(f"[DEBUG][{time.time():.3f}] eid={eid} right-click -> dash")
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='dash')
            self.prev_right[eid] = curr_right
