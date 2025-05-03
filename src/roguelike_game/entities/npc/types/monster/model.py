# Path: src/roguelike_game/entities/npc/types/monster/model.py

import math
from src.roguelike_game.entities.npc.base.model import BaseNPCModel
from src.roguelike_game.entities.npc.utils.geometry import calculate_distance

class MonsterModel(BaseNPCModel):
    def __init__(self, x: float, y: float, name: str = "Monster"):
        super().__init__(x, y, name)
        # Stats por defecto (se pueden sobrescribir desde config.yaml)
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
        self.direction = (0, 1)
        self.alive = True

    def take_damage(self, amount: float):
        super().take_damage(amount)
