# Path: src/roguelike_game/entities/load_obstacles.py
from roguelike_game.entities.obstacles.obstacle import Obstacle

def load_obstacles():
    return [
        Obstacle(800, 700),
        Obstacle(800, 800),
        # 🧩 Podés agregar más aquí o cargar desde archivo en el futuro
    ]