### 🔁 Archivo: roguelike_project/entities/projectiles/base.py

import pygame
import math

class Projectile:
    def __init__(self, x, y, angle, speed, lifespan, sprite, size=(32, 32)):
        self.x = x
        self.y = y
        self.angle = angle
        self.speed = speed
        self.lifespan = lifespan
        self.sprite = pygame.transform.scale(sprite, size)
        self.size = size

        radians = math.radians(angle)
        self.dx = math.cos(radians) * speed
        self.dy = math.sin(radians) * speed

        self.rect = pygame.Rect(self.x, self.y, *self.size)
        self.alive = True
        self.damage = 10

    def update(self, solid_tiles=None, enemies=None):
        self.x += self.dx
        self.y += self.dy
        self.lifespan -= 1
        self.rect.topleft = (self.x, self.y)

        # Colisión con tiles sólidos
        if solid_tiles:
            for tile in solid_tiles:
                if tile.solid and self.rect.colliderect(tile.rect):
                    self.alive = False
                    return

        # Colisión con enemigos
        if enemies:
            for enemy in enemies:
                if self.rect.colliderect(enemy.hitbox):
                    if hasattr(enemy, 'take_damage'):
                        enemy.take_damage(self.damage)
                    self.alive = False
                    return

        if self.lifespan <= 0:
            self.alive = False

    def render(self, screen, camera):
        screen.blit(self.sprite, camera.apply((self.x, self.y)))