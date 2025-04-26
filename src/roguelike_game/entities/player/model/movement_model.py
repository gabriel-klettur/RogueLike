# src/roguelike_game/entities/player/model/movement_model.py

import pygame
import math
import time

class PlayerMovement:
    """
    Modelo de movimiento: walking, dash y teleport con colisión.
    """
    def __init__(self, player):
        self.player = player
        self.speed = 10
        # Dash
        self.is_dashing = False
        self.dash_speed = 2000
        self.dash_duration = 0.2
        self.last_dash_time = -math.inf
        self.dash_cooldown = 2.0
        self.dash_time_left = 0.0
        self.dash_direction = pygame.Vector2(0,0)
        # Teleport
        self.teleport_cooldown = 0.5
        self.last_teleport_time = -math.inf
        self.teleport_distance = 1000

    def hitbox(self, x=None, y=None):
        """
        Caja de colisión completa basada en sprite_size.
        """
        px = x if x is not None else self.player.x
        py = y if y is not None else self.player.y
        w, h = self.player.sprite_size
        return pygame.Rect(px, py, w, h)

    def move(self, dx, dy, collision_tiles, obstacles):
        """
        Movimiento básico con detección de colisión contra tiles y obstáculos.
        """
        # Normalizar diagonal
        if dx != 0 and dy != 0:
            norm = math.hypot(dx, dy)
            dx, dy = dx / norm, dy / norm
        # Calcular hitbox futura
        new_x = self.player.x + dx * self.speed
        new_y = self.player.y + dy * self.speed
        future = self.hitbox(new_x, new_y)
        # Verificar colisión con tiles sólidos
        for tile in collision_tiles:
            if hasattr(tile, 'rect') and future.colliderect(tile.rect):
                return  # Abortamos movimiento
        # Verificar colisión con obstáculos
        for ob in obstacles:
            if future.colliderect(ob.rect):
                return
        # Si no colisiona, aplicamos el movimiento
        self.player.x = new_x
        self.player.y = new_y
        self.player.is_walking = (dx != 0 or dy != 0)

    def start_dash(self, direction_vec):
        now = time.time()
        if now - self.last_dash_time < self.dash_cooldown:
            return False
        self.is_dashing = True
        self.dash_time_left = self.dash_duration
        if direction_vec.length():
            self.dash_direction = direction_vec.normalize()
        else:
            self.dash_direction = pygame.Vector2(0,0)
        self.last_dash_time = now
        return True

    def update_dash(self, collision_tiles, obstacles):
        if not self.is_dashing:
            return
        delta = pygame.time.get_ticks() / 1000.0
        dist = self.dash_speed * delta
        dx = self.dash_direction.x * dist
        dy = self.dash_direction.y * dist
        # Intentar movimiento de dash con las mismas colisiones
        self.move(dx / self.speed, dy / self.speed, collision_tiles, obstacles)
        self.dash_time_left -= delta
        if self.dash_time_left <= 0:
            self.is_dashing = False

    def teleport(self, tx, ty):
        now = time.time()
        if now - self.last_teleport_time < self.teleport_cooldown:
            return False
        px = self.player.x + self.player.sprite_size[0] / 2
        py = self.player.y + self.player.sprite_size[1] / 2
        vec = pygame.Vector2(tx - px, ty - py)
        if vec.length():
            vec.normalize_ip()
        self.player.x += vec.x * self.teleport_distance
        self.player.y += vec.y * self.teleport_distance
        self.last_teleport_time = now
        return True
