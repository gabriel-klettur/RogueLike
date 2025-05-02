from src.roguelike_game.entities.player.controller.player_controller import PlayerController
from src.roguelike_game.entities.load_obstacles import load_obstacles
from src.roguelike_game.entities.load_hostile   import load_hostile
from src.roguelike_game.entities.load_buildings import load_buildings

def load_entities(z_state=None):
    obstacles = load_obstacles()
    player_ctrl = PlayerController(600, 600, z_state, obstacles=obstacles)
    buildings   = load_buildings(z_state)
    enemies     = load_hostile()
    return player_ctrl, obstacles, buildings, enemies