# Path: roguelike_project/entities/projectiles/fireball.py

import pygame
from roguelike_project.entities.projectiles.base import Projectile
from roguelike_project.utils.loader import load_image

class Fireball(Projectile):
    def __init__(self, x, y, angle):
        sprite = load_image("assets/projectiles/fireball.png", (32, 32))
        speed = 10           # Velocidad de la bola de fuego
        lifespan = 60        # Dura 60 frames (~1 segundo a 60fps)
        super().__init__(x, y, angle, speed, lifespan, sprite)
        self.damage = 10     # Daño opcional
