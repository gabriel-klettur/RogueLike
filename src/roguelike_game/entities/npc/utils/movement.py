# Path: src/roguelike_game/entities/npc/utils/movement.py

import math
import pygame

class NPCMovement:
    """
    Movimiento para NPCs con colisión y 'sliding' a lo largo de paredes.
    """
    def __init__(self, npc_model):
        self.npc = npc_model

    def hitbox(self, x=None, y=None) -> pygame.Rect:
        px = x if x is not None else self.npc.x
        py = y if y is not None else self.npc.y
        w, h = self.npc.sprite_size

        foot_h = int(h * 0.25)
        foot_w = int(w * 0.5)
        foot_x = px + (w - foot_w) // 2
        foot_y = py + h - foot_h

        return pygame.Rect(foot_x, foot_y, foot_w, foot_h)

    def _collides(self, future: pygame.Rect, collision_tiles: list, obstacles: list) -> bool:
        for tile in collision_tiles:
            if getattr(tile, "solid", False) and future.colliderect(tile.rect):
                return True
        for ob in obstacles:
            if future.colliderect(ob.rect):
                return True
        return False

    def move(self, dx: float, dy: float, collision_tiles: list, obstacles: list):
        # Normalizar diagonal
        if dx != 0 and dy != 0:
            norm = math.hypot(dx, dy)
            dx, dy = dx / norm, dy / norm

        speed = self.npc.speed
        x0, y0 = self.npc.x, self.npc.y

        # Intento 1: movimiento completo
        nx, ny = x0 + dx * speed, y0 + dy * speed
        future = self.hitbox(nx, ny)
        if not self._collides(future, collision_tiles, obstacles):
            self.npc.x, self.npc.y = nx, ny
            self.npc.direction = (dx, dy)
            return

        # Intento 2: solo X
        nx, ny = x0 + dx * speed, y0
        future_x = self.hitbox(nx, ny)
        if not self._collides(future_x, collision_tiles, obstacles):
            self.npc.x = nx
            self.npc.direction = (dx, 0)
            return

        # Intento 3: solo Y
        nx, ny = x0, y0 + dy * speed
        future_y = self.hitbox(nx, ny)
        if not self._collides(future_y, collision_tiles, obstacles):
            self.npc.y = ny
            self.npc.direction = (0, dy)
            return

        # Si colisiona en todos los casos, se queda quieto
