from roguelike_project.entities.player.player import Player
from roguelike_project.loaders.load_obstacles import load_obstacles
from roguelike_project.loaders.load_enemies import load_enemies
from roguelike_project.loaders.load_buildings import load_buildings

def load_entities(z_state=None):
    player = Player(600, 600)
    obstacles = load_obstacles()
    buildings = load_buildings(z_state)  # ✅ PASARLO
    enemies = load_enemies()
    return player, obstacles, buildings, enemies
