# Path: src/roguelike_game/entities/npc/types/elite/controller.py

from src.roguelike_game.entities.npc.types.monster.controller import MonsterController
from .model import EliteModel

class EliteController(MonsterController):
    def __init__(self, model: EliteModel):
        super().__init__(model)
        # Aquí podrías añadir lógica extra de IA para élites
