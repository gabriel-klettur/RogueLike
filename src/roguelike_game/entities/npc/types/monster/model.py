# Path: src/roguelike_game/entities/npc/types/monster/model.py

from src.roguelike_game.entities.npc.interfaces import IModel

class MonsterModel(IModel):
    def __init__(self, x: float, y: float, name: str = "Monster"):
        self.x = x
        self.y = y
        self.name = name
        self.health = 60
        self.max_health = 60
        self.speed = 5.0

        # Patrulla: [(dx,dy,dist), …]
        self.path = [
            (0, -1, 200),
            (1,  0,  50),
            (0,  1, 200),
            (-1, 0,  50),
        ]
        self.current_step = 0
        self.step_progress = 0.0

        # IA
        self.direction = (0, 1)
        self.alive = True

    def take_damage(self, amount: float):
        self.health -= amount
        if self.health <= 0:
            self.alive = False
