import pygame
import time

class PlayerAttack:
    def __init__(self, player):
        self.player = player
        self.last_attack_time = 0  # Usado por el HUD
        self.attack_cooldown = 0.2  # medio segundo entre ataques

    def perform_basic_attack(self):
        now = time.time()
        if now - self.last_attack_time < self.attack_cooldown:
            return
        self.last_attack_time = now

        # Posición inicial desde el centro del jugador
        x = self.player.x + self.player.sprite_size[0] // 2
        y = self.player.y + self.player.sprite_size[1]  # pies

        # Dirección según orientación del jugador
        direction_map = {
            "up": pygame.Vector2(0, -1),
            "down": pygame.Vector2(0, 1),
            "left": pygame.Vector2(-1, 0),
            "right": pygame.Vector2(1, 0),
        }
        direction = direction_map.get(self.player.direction, pygame.Vector2(1, 0))

        self.player.state.combat.effects.spawn_slash_arc(self.player, direction)
