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
        self.mask = pygame.mask.from_surface(self.sprite)
        self.alive = True
        self.damage = 10
        self.on_explode = None

    def update(self, solid_tiles=None, enemies=None):
        self.x += self.dx
        self.y += self.dy
        self.lifespan -= 1
        self.rect.topleft = (self.x, self.y)

        if self.lifespan <= 0:
            self.alive = False
            return

        if enemies:
            for enemy in enemies:
                if not hasattr(enemy, "mask") or not enemy.alive:
                    continue

                offset = (int(enemy.x - self.x), int(enemy.y - self.y))
                if self.mask.overlap(enemy.mask, offset):
                    if hasattr(enemy, 'take_damage'):
                        enemy.take_damage(self.damage)
                    if self.on_explode:
                        self.on_explode(self.x, self.y)
                    self.alive = False
                    return

        if solid_tiles:
            for tile in solid_tiles:
                if tile.solid and self.rect.colliderect(tile.rect):
                    if self.on_explode:
                        self.on_explode(self.x, self.y)
                    self.alive = False
                    return

    def render(self, screen, camera):
        if not self.alive:
            return

        if not camera.is_in_view(self.x, self.y, self.size):  # ✅ Visibilidad
            return

        screen.blit(self.sprite, camera.apply((self.x, self.y)))
