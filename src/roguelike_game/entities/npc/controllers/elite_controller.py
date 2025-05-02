# Path: src/roguelike_game/entities/npc/controllers/elite_controller.py
from src.roguelike_game.entities.npc.controllers.monster_controller import MonsterController
from src.roguelike_game.entities.npc.models.elite_model import EliteModel

class EliteController(MonsterController):
    def __init__(self, model: EliteModel):
        super().__init__(model)
        # Podrías añadir IA específica aquí