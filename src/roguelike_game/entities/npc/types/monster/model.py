# Path: src/roguelike_game/entities/npc/types/monster/model.py

from src.roguelike_game.entities.npc.base.model import BaseNPCModel
from src.roguelike_game.entities.npc.utils.movement import NPCMovement
from src.roguelike_game.entities.npc.types.monster.view import MonsterView

class MonsterModel(BaseNPCModel):
    """
    Modelo de monstruo que ahora incluye un componente de movimiento
    con colisión y conoce su tamaño de sprite para el hitbox.
    """
    def __init__(self, x: float, y: float, name: str = "Monster"):
        super().__init__(x, y, name)

        # Stats por defecto (pueden sobrescribirse desde YAML)
        self.health = 60
        self.max_health = 60
        self.speed = 5.0

        # Componente de movimiento con detección de colisión
        self.movement = NPCMovement(self)

        # Tamaño del sprite (necesario para calcular el hitbox de pies)
        # Lo tomamos de la vista, que define SPRITE_SIZE
        self.sprite_size = MonsterView.SPRITE_SIZE

        # Patrulla: lista de (dx, dy, distancia)
        self.path = [
            (0, -1, 200),
            (1,  0,  50),
            (0,  1, 200),
            (-1, 0,  50),
        ]
        self.current_step = 0
        self.step_progress = 0.0
        self.direction = (0, 1)
        self.alive = True

    def take_damage(self, amount: float):
        
        super().take_damage(amount)
