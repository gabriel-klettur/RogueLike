import math
import pygame
from roguelike_game.entities.npc.base.controller import BaseNPCController
from roguelike_game.entities.npc.types.monster.model import MonsterModel

# Distancia de seguimiento al jugador
FOLLOW_DIST_SQ = 500 * 500
EPSILON = 1e-6  # Para evitar normalización en distancias mínimas

# --- Funciones auxiliares optimizadas ---

def filter_nearby(entities, origin_x, origin_y, radius_sq):
    """Filtra obstáculos cercanos para evitar pasar listas completas."""
    result = []
    for e in entities:
        dx, dy = e.x - origin_x, e.y - origin_y
        if dx * dx + dy * dy <= radius_sq:
            result.append(e)
    return result

def follow_player_logic(model, movement, px, py, player_hitbox, collision_tiles, obstacles):
    dx = px - model.x
    dy = py - model.y
    dist_sq = dx * dx + dy * dy

    if dist_sq < EPSILON:
        return

    vec = pygame.Vector2(dx, dy)
    vec.normalize_ip()

    nx = model.x + vec.x * model.speed
    ny = model.y + vec.y * model.speed
    future_hitbox = movement.hitbox(nx, ny)

    if future_hitbox.colliderect(player_hitbox):
        return

    movement.move(vec.x, vec.y, collision_tiles, obstacles)

def patrol_logic(model, movement, patrol_moves, collision_tiles, obstacles, speed):
    step = model.current_step
    dx_move, dy_move, length = patrol_moves[step]

    movement.move(dx_move, dy_move, collision_tiles, obstacles)
    model.step_progress += speed
    model.direction = (dx_move / speed, dy_move / speed)

    if model.step_progress >= length:
        model.current_step = (step + 1) % len(patrol_moves)
        model.step_progress = 0.0

# --- Controlador del monstruo ---

class MonsterController(BaseNPCController):
    """
    Controlador de monstruo:
    Patrulla un camino hasta que detecta al jugador, entonces lo persigue.
    Se detiene si va a colisionar con él.
    """
    __slots__ = ('movement', 'patrol_moves')

    def __init__(self, model: MonsterModel):
        super().__init__(model)
        self.movement = model.movement
        speed = model.speed
        self.patrol_moves = [
            (dx * speed, dy * speed, length)
            for dx, dy, length in model.path
        ]

    def update(self, state, map, entities):
        m = self.model
        if not m.alive:
            return

        px, py = entities.player.x, entities.player.y
        dx = px - m.x
        dy = py - m.y
        dist_sq = dx * dx + dy * dy

        # Pre-filtrar obstáculos por proximidad (mejora en mapas grandes)
        collision_tiles = filter_nearby(map.solid_tiles, m.x, m.y, FOLLOW_DIST_SQ)
        obstacles = filter_nearby(entities.obstacles, m.x, m.y, FOLLOW_DIST_SQ)

        if dist_sq <= FOLLOW_DIST_SQ:
            player_hitbox = entities.player.hitbox()  # solo si necesario
            self._follow_player(px, py, player_hitbox, collision_tiles, obstacles)
        else:
            self._patrol(collision_tiles, obstacles)

    def _follow_player(self, px, py, player_hitbox, collision_tiles, obstacles):
        follow_player_logic(self.model, self.movement, px, py, player_hitbox, collision_tiles, obstacles)

    def _patrol(self, collision_tiles, obstacles):
        patrol_logic(self.model, self.movement, self.patrol_moves, collision_tiles, obstacles, self.model.speed)
