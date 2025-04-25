from src.roguelike_project.engine.game.entities.obstacles.obstacle import Obstacle

def load_obstacles():
    return [
        Obstacle(300, 700),
        Obstacle(600, 725),
        # 🧩 Podés agregar más aquí o cargar desde archivo en el futuro
    ]
