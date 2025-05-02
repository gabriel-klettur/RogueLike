# Path: src/roguelike_game/entities/npc/models/monster_model.py
import math
from src.roguelike_game.entities.npc.interfaces import IModel

class MonsterModel(IModel):
    def __init__(self, x: float, y: float, name: str = "Monster"):
        self.x = x
        self.y = y
        self.name = name
        self.health = 60
        self.max_health = 60
        self.speed = 5.0
        # Patrulla: lista de tuplas (dx, dy, distancia)
        self.path = [
            (0, -1, 200),
            (1,  0,  50),
            (0,  1, 200),
            (-1, 0,  50),
        ]
        self.current_step = 0
        self.step_progress = 0.0
        # Dirección actual para la vista
        self.direction = (0, 1)
        self.alive = True

    def take_damage(self, amount: float):
        self.health -= amount
        if self.health <= 0:
            self.alive = False

    def calculate_distance(self, px: float, py: float) -> float:
        return math.hypot(px - self.x, py - self.y)