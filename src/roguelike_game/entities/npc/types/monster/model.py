# Path: src/roguelike_game/entities/npc/types/monster/model.py

from roguelike_game.entities.npc.base.model import BaseNPCModel
from roguelike_game.entities.npc.utils.movement import NPCMovement
from roguelike_game.entities.npc.types.monster.view import MonsterView

class MonsterModel(BaseNPCModel):
    def __init__(self, x: float, y: float, name: str = "Monster"):
        super().__init__(x, y, name)

        # Stats por defecto
        self.health = 60
        self.max_health = 60
        self.speed = 5.0

        # Componente de movimiento con colisión
        self.movement = NPCMovement(self)

        # Tamaño del sprite (usado para calcular la hitbox)
        self.sprite_size = MonsterView.SPRITE_SIZE

        # --- Parámetros de hitbox personalizables ---
        # anchura y altura de la caja de colisión de pies:
        self.hitbox_width  = int(self.sprite_size[0] * 0.5)
        self.hitbox_height = int(self.sprite_size[1] * 0.25)
        # offset desde la esquina superior izquierda:
        #   (hacia la derecha, hacia abajo)
        self.hitbox_offset_x = (self.sprite_size[0] - self.hitbox_width) // 2
        self.hitbox_offset_y = self.sprite_size[1] - self.hitbox_height

        # Patrulla
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
