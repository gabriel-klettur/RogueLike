#Path: src/roguelike_game/entities/npc/utils/movement.py

import math
import pygame

class NPCMovement:
    """
    Movimiento para NPCs con detección de colisión AABB
    usando un 'hitbox' de pies personalizable.
    """
    def __init__(self, npc_model):
        self.npc = npc_model

    def hitbox(self, x=None, y=None) -> pygame.Rect:
        """
        Rectángulo que cubre sólo la parte de los pies del sprite,
        con tamaño y offset personalizables desde el modelo.
        """
        px = x if x is not None else self.npc.x
        py = y if y is not None else self.npc.y
        w, h = self.npc.sprite_size

        # Tamaño de la hitbox (anchura/altura)
        bw = getattr(self.npc, "hitbox_width", int(w * 0.5))
        bh = getattr(self.npc, "hitbox_height", int(h * 0.25))

        # Offset relativo desde la esquina superior izquierda del sprite
        ox = getattr(self.npc, "hitbox_offset_x", (w - bw) // 2)
        oy = getattr(self.npc, "hitbox_offset_y", h - bh)

        return pygame.Rect(px + ox, py + oy, bw, bh)

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

        # Intento 2: sólo X
        nx, ny = x0 + dx * speed, y0
        future = self.hitbox(nx, ny)
        if not self._collides(future, collision_tiles, obstacles):
            self.npc.x = nx
            self.npc.direction = (dx, 0)
            return

        # Intento 3: sólo Y
        nx, ny = x0, y0 + dy * speed
        future = self.hitbox(nx, ny)
        if not self._collides(future, collision_tiles, obstacles):
            self.npc.y = ny
            self.npc.direction = (0, dy)
            return

        # Si colisiona en las tres direcciones, no se mueve
