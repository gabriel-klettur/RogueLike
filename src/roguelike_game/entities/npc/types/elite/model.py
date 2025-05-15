# Path: src/roguelike_game/entities/npc/types/elite/model.py
from roguelike_game.entities.npc.types.elite.view import EliteView
from roguelike_game.entities.npc.types.monster.model import MonsterModel

class EliteModel(MonsterModel):
    def __init__(self, x: float, y: float, name: str = "Elite", sprite_size=None):
        super().__init__(x, y, name, sprite_size=sprite_size)

        # 1️⃣ Usar el tamaño de sprite pasado o el de la vista por defecto
        if sprite_size is not None:
            self.sprite_size = sprite_size
        else:
            self.sprite_size = EliteView.SPRITE_SIZE

        # 2️⃣ Recalcular hitbox según ese tamaño
        self.hitbox_width  = int(self.sprite_size[0] * 0.5)
        self.hitbox_height = int(self.sprite_size[1] * 0.25)
        self.hitbox_offset_x = (self.sprite_size[0] - self.hitbox_width) // 2
        self.hitbox_offset_y = self.sprite_size[1] - self.hitbox_height

        # 3️⃣ Tus stats de élite
        self.health     = 100
        self.max_health = 100
        self.speed      = 6.0