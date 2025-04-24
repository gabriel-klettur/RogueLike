# File: roguelike_project/systems/combat/spells/healing_aura/model.py
import pygame
import random

class HealingParticle:
    """
    Representa una única partícula del aura de sanación.
    Solo contiene datos y lógica interna mínima (edad, posición relativa).
    """
    def __init__(self, player, offset_x):
        self.player = player
        self.offset = pygame.Vector2(offset_x, 0)
        self.age = 0
        self.lifespan = 60            # Duración de la partícula en frames
        self.size = random.randint(4, 8)
        self.color = random.choice([
            (0, 255, 100),
            (100, 255, 150),
            (0, 200, 100)
        ])
        # Velocidad extra inversa al movimiento del jugador
        if player.is_walking:
            self.extra_velocity = -0.5 * player.movement.last_move_dir
        else:
            self.extra_velocity = pygame.Vector2(0, 0)

    def update(self):
        """Incrementa la vida de la partícula."""
        self.age += 1

    def is_dead(self):
        return self.age >= self.lifespan

    def get_world_pos(self):
        """Calcula su posición absoluta en el mundo. Returns pygame.Vector2."""
        sprite_w = self.player.sprite_size[0]
        base_pos = pygame.Vector2(
            self.player.x + sprite_w / 2,
            self.player.y + 120
        )
        vertical_rise = pygame.Vector2(0, -self.age * 2.0)
        horizontal_shift = self.extra_velocity * self.age
        return base_pos + self.offset + vertical_rise + horizontal_shift

class HealingAuraModel:
    """
    Modelo puro: contiene estado de la aura (timer, particles).
    """
    def __init__(self, player):
        self.player = player
        self.particles: list[HealingParticle] = []
        self.timer = 0
        self.duration = 120          # Frames de generación de partículas
        self.elipse_lifespan = 100   # Frames de duración del óvalo base

    def is_empty(self) -> bool:
        return self.timer >= self.duration and not self.particles