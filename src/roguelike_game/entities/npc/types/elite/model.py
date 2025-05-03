# Path: src/roguelike_game/entities/npc/types/elite/model.py

from src.roguelike_game.entities.npc.types.monster.model import MonsterModel

class EliteModel(MonsterModel):
    def __init__(self, x: float, y: float, name: str = "Elite"):
        super().__init__(x, y, name)
        # Los valores por defecto quedan aquí, luego YAML los sobreescribe
        self.health = 100
        self.max_health = 100
        self.speed = 6.0
