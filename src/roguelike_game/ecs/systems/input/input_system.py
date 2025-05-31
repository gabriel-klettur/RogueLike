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
from roguelike_game.config_player import PLAYER_SPEED
from roguelike_engine.utils.benchmark import benchmark

class InputSystem:
    """
    Captura el estado del teclado y actualiza InputComponent y Velocity.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.InputSystem.update")
    def update(self, world, camera=None):
        # Obtener estado actual del teclado
        keys = pygame.key.get_pressed()
        # Para cada entidad con InputComponent
        for eid, inp in world.components.get('InputComponent', {}).items():
            # Movimiento en ejes X e Y
            inp.move_x = int(keys[pygame.K_RIGHT]) - int(keys[pygame.K_LEFT])
            inp.move_y = int(keys[pygame.K_DOWN]) - int(keys[pygame.K_UP])
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
            # Mapear habilidades Q, E y click
            inp.skill_q = bool(keys[pygame.K_q])
            inp.skill_e = bool(keys[pygame.K_e])
            inp.click = bool(pygame.mouse.get_pressed()[0])
            # Generar intenciones de hechizo
            if inp.skill_q:
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='firework')
                inp.skill_q = False
            if inp.skill_e:
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='smoke')
                inp.skill_e = False
            if inp.click:
                world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='pixel_fire')
                inp.click = False
            # Procesar ataque: tecla SPACE
            if keys[pygame.K_SPACE]:
                now = time.time()
                # Verificar cooldown
                cd = world.components.get('AttackCooldown', {}).get(eid)
                weapon = world.components.get('MeleeWeapon', {}).get(eid)
                cooldown_time = weapon.cooldown if weapon else 1.0
                if cd is None or now >= cd.next_time:
                    # Seleccionar primer objetivo distinto
                    for target in world.components.get('CombatStats', {}):
                        if target != eid:
                            world.components.setdefault('WantsToMelee', {})[eid] = WantsToMelee(attacker=eid, target=target)
                            world.components.setdefault('AttackCooldown', {})[eid] = AttackCooldown(now + cooldown_time)
                            break
