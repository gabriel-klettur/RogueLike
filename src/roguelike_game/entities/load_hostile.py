from src.roguelike_game.entities.npc.hostile.monster import Monster

def load_hostile():
    return [
        Monster(800, 600),
        Monster(800, 800),
    ]
