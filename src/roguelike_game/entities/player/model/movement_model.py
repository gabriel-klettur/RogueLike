
# Path: src/roguelike_game/entities/player/model/movement_model.py
import math
import pygame

from roguelike_game.entities.player.config_player import (
    PLAYER_SPEED,
    PLAYER_DASH_SPEED,
    PLAYER_DASH_COOLDOWN,
    PLAYER_DASH_DURATION,
    PLAYER_TELEPORT_COOLDOWN,
    PLAYER_TELEPORT_DISTANCE,
)

class PlayerMovement:
    """
    Modelo de movimiento: walking, dash y teleport con colisión.
    Con wall-sliding para evitar quedarse atascado en esquinas.
    """
    def __init__(self, player):
        self.player = player
        self.speed = PLAYER_SPEED

        # Dash
        self.is_dashing = False
        self.dash_speed = PLAYER_DASH_SPEED
        self.dash_duration = PLAYER_DASH_DURATION
        self.last_dash_time = -math.inf
        self.dash_cooldown = PLAYER_DASH_COOLDOWN
        self.dash_time_left = 0.0
        self.dash_direction = pygame.Vector2(0, 0)

        # Teleport
        self.teleport_cooldown = PLAYER_TELEPORT_COOLDOWN
        self.last_teleport_time = -math.inf
        self.teleport_distance = PLAYER_TELEPORT_DISTANCE

    def hitbox(self, x=None, y=None) -> pygame.Rect:
        """
        Foot-only hitbox para colisiones: rectángulo centrado en la parte inferior.
        """
        px = x if x is not None else self.player.x
        py = y if y is not None else self.player.y
        w, h = self.player.sprite_size
        foot_h = int(h * 0.25)
        foot_w = int(w * 0.5)
        foot_x = px + (w - foot_w) // 2
        foot_y = py + h - foot_h
        return pygame.Rect(foot_x, foot_y, foot_w, foot_h)

    def move(self, dx, dy, collision_tiles, obstacles):
        """
        Movimiento con colisión y wall sliding:
        - Primero intenta movimiento diagonal completo.
        - Si choca, prueba solo horizontal.
        - Luego solo vertical.
        """
        # Normalizar diagonal
        if dx != 0 and dy != 0:
            norm = math.hypot(dx, dy)
            dx, dy = dx / norm, dy / norm

        speed = self.speed  # usar atributo de PlayerMovement, no de PlayerModel
        x0, y0 = self.player.x, self.player.y

        # Intento 1: movimiento completo
        nx, ny = x0 + dx * speed, y0 + dy * speed
        future = self.hitbox(nx, ny)
        if not self._collides(future, collision_tiles, obstacles):
            self.player.x, self.player.y = nx, ny
            self.player.is_walking = (dx != 0 or dy != 0)
            return

        # Intento 2: solo X
        nx, ny = x0 + dx * speed, y0
        future = self.hitbox(nx, ny)
        if not self._collides(future, collision_tiles, obstacles):
            self.player.x = nx
            self.player.is_walking = (dx != 0)
            return

        # Intento 3: solo Y
        nx, ny = x0, y0 + dy * speed
        future = self.hitbox(nx, ny)
        if not self._collides(future, collision_tiles, obstacles):
            self.player.y = ny
            self.player.is_walking = (dy != 0)
            return

        # Si colisiona en las tres direcciones, no camina
        self.player.is_walking = False

    def _collides(self, future: pygame.Rect, collision_tiles, obstacles) -> bool:
        for tile in collision_tiles:
            if getattr(tile, "solid", False) and future.colliderect(tile.rect):
                return True
        for ob in obstacles:
            if future.colliderect(ob.rect):
                return True
        return False

    def start_dash(self, direction_vec):
        now = pygame.time.get_ticks() / 1000.0
        if now - self.last_dash_time < self.dash_cooldown:
            return False
        self.is_dashing = True
        self.dash_time_left = self.dash_duration
        self.dash_direction = direction_vec.normalize() if direction_vec.length() else pygame.Vector2(0, 0)
        self.last_dash_time = now
        return True

    def update_dash(self, collision_tiles, obstacles):
        if not self.is_dashing:
            return
        # Use the game's clock to get the time elapsed since the last frame
        # rather than the time since pygame was initialised. Using
        # ``pygame.time.get_ticks`` caused the dash distance to grow
        # continuously over time.
        delta_time = self.player.state.clock.get_time() / 1000.0
        dist = self.dash_speed * delta_time / self.speed
        dx = self.dash_direction.x * dist
        dy = self.dash_direction.y * dist
        # Reutiliza move() con wall sliding
        self.move(dx, dy, collision_tiles, obstacles)
        self.dash_time_left -= delta_time
        if self.dash_time_left <= 0:
            self.is_dashing = False

    def teleport(self, tx, ty):
        now = pygame.time.get_ticks() / 1000.0
        if now - self.last_teleport_time < self.teleport_cooldown:
            return False
        # Centro actual del jugador
        px = self.player.x + self.player.sprite_size[0] / 2
        py = self.player.y + self.player.sprite_size[1] / 2
        vec = pygame.Vector2(tx - px, ty - py)
        if vec.length():
            vec.normalize_ip()
        # Teletransportar
        self.player.x += vec.x * self.teleport_distance
        self.player.y += vec.y * self.teleport_distance
        self.last_teleport_time = now
        return True