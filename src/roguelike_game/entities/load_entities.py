# Path: src/roguelike_game/entities/load_entities.py

from roguelike_game.entities.load_obstacles import load_obstacles
from roguelike_game.entities.load_buildings import load_buildings

def load_entities(z_state=None):
    obstacles = load_obstacles()    
    buildings   = load_buildings(z_state)
    
    return  obstacles, buildings