# Path: src/roguelike_game/entities/npc/models/elite_model.py
from src.roguelike_game.entities.npc.models.monster_model import MonsterModel

class EliteModel(MonsterModel):
    def __init__(self, x: float, y: float, name: str = "Elite"):
        super().__init__(x, y, name)
        # Ajustar stats de élite
        self.health = 100
        self.max_health = 100
        self.speed = 6.0