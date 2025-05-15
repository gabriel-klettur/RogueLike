
# Path: src/roguelike_game/entities/npc/types/monster/model.py
from roguelike_game.entities.npc.base.model import BaseNPCModel
from roguelike_game.entities.npc.utils.movement import NPCMovement
from roguelike_game.entities.npc.types.monster.view import MonsterView

class MonsterModel(BaseNPCModel):
    def __init__(self, x: float, y: float, name: str = "Monster", sprite_size=None):
        super().__init__(x, y, name)

        # Stats por defecto
        self.health = 60
        self.max_health = 60
        self.speed = 5.0

        # Componente de movimiento con colisión
        self.movement = NPCMovement(self)

        # Tamaño del sprite (usado para calcular la hitbox)
        if sprite_size is not None:
            self.sprite_size = sprite_size
        else:
            self.sprite_size = MonsterView.SPRITE_SIZE

        # --- Parámetros de hitbox personalizables (proporcionales a sprite_size) ---
        # Permite definir proporciones desde el YAML (por ejemplo, 0.5 equivale a la mitad del ancho)
        width_prop = getattr(self, 'hitbox_width_prop', 0.5)
        height_prop = getattr(self, 'hitbox_height_prop', 0.25)
        offset_x_prop = getattr(self, 'hitbox_offset_x_prop', (1.0 - width_prop) / 2)
        offset_y_prop = getattr(self, 'hitbox_offset_y_prop', 1.0 - height_prop)
        self.hitbox_width  = int(self.sprite_size[0] * width_prop)
        self.hitbox_height = int(self.sprite_size[1] * height_prop)
        self.hitbox_offset_x = int(self.sprite_size[0] * offset_x_prop)
        self.hitbox_offset_y = int(self.sprite_size[1] * offset_y_prop)

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