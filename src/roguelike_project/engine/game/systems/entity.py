from src.roguelike_game.entities.player.player import Player
from src.roguelike_project.engine.game.loaders.load_obstacles import load_obstacles
from src.roguelike_project.engine.game.loaders.load_enemies import load_enemies
from src.roguelike_project.engine.game.loaders.load_buildings import load_buildings

def load_entities(z_state=None):
    player = Player(600, 600)
    obstacles = load_obstacles()
    buildings = load_buildings(z_state)
    enemies = load_enemies()
    return player, obstacles, buildings, enemies
