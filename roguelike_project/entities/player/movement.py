import pygame
import math


class PlayerMovement:
    def __init__(self, player):
        self.player = player
        self.speed = 10
        self.is_moving = False 
        self.last_move_dir = pygame.Vector2(0, 0)  # 🧭 Dirección del último movimiento

    def move(self, dx, dy, obstacles, solid_tiles):
        self.is_moving = False  # Reseteamos al inicio

        if dx != 0 or dy != 0:
            self.update_direction(dx, dy)

            # Normalizamos si hay movimiento diagonal
            if dx != 0 and dy != 0:
                norm = math.sqrt(dx ** 2 + dy ** 2)
                dx /= norm
                dy /= norm

            # Guardamos la dirección del movimiento normalizado
            self.last_move_dir = pygame.Vector2(dx, dy)
        else:
            self.last_move_dir = pygame.Vector2(0, 0)

        collided = False

        # Hitbox futura con movimiento aplicado
        future_hitbox = self.get_hitbox(
            self.player.x + dx * self.speed,
            self.player.y + dy * self.speed
        )

        # Colisión con tiles sólidos
        for tile in solid_tiles:
            if future_hitbox.colliderect(tile.rect):
                collided = True
                break

        # Colisión con obstáculos
        if not collided:
            for ob in obstacles:
                if future_hitbox.colliderect(ob.rect):
                    collided = True
                    break

        # Movimiento si no hay colisión
        if not collided:
            self.player.x += dx * self.speed
            self.player.y += dy * self.speed
            self.is_moving = True  # ✅ Movimiento permitido
            if self.player.hitbox is None:
                self.player.hitbox = self.get_hitbox(self.player.x, self.player.y)
            else:
                self.player.hitbox.topleft = (self.player.x + 20, self.player.y + 96)
        else:
            self.player.take_damage()

    def get_hitbox(self, x, y):
        return pygame.Rect(x + 20, y + 96, 56, 28)

    def update_direction(self, dx, dy):
        if dx == -1:
            self.player.direction = "left"
        elif dx == 1:
            self.player.direction = "right"
        elif dy == -1:
            self.player.direction = "up"
        elif dy == 1:
            self.player.direction = "down"
