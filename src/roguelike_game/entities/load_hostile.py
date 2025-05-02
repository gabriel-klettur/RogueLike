# Path: src/roguelike_game/entities/load_hostile.py
from src.roguelike_game.entities.npc.models.monster import Monster
from src.roguelike_game.entities.npc.models.elite_monster import Elite
def load_hostile():
    return [
        Monster(800, 600),
        Monster(800, 800),
        Elite(800, 1000),
    ]