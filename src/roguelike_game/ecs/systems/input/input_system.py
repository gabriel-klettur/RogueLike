"""
Module: input_system.py
Sistema que traduce el estado de teclado a InputComponent y actualiza Velocity.
"""
import pygame
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.entities.player.config_player import PLAYER_SPEED

class InputSystem:
    """
    Captura el estado del teclado y actualiza InputComponent y Velocity.
    """
    def __init__(self):
        pass

    def update(self, world):
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
                vel.vx = inp.move_x * ms.speed
                vel.vy = inp.move_y * ms.speed
