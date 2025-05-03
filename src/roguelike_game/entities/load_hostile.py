# Path: src/roguelike_game/entities/load_hostile.py
from roguelike_game.entities.npc.factory import NPCFactory

def load_hostile():
    return [
        NPCFactory.create("monster", x=800, y=600),
        NPCFactory.create("monster", x=800, y=800),
        NPCFactory.create("elite",   x=800, y=1000),
    ]