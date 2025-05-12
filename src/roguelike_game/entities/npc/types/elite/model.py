# Al principio del archivo:
from roguelike_game.entities.npc.types.elite.view import EliteView
from roguelike_game.entities.npc.types.monster.model import MonsterModel

class EliteModel(MonsterModel):
    def __init__(self, x: float, y: float, name: str = "Elite"):
        super().__init__(x, y, name)

        # 1️⃣ Usar el mismo tamaño de sprite que la vista
        self.sprite_size = EliteView.SPRITE_SIZE  # (512, 512)

        # 2️⃣ Recalcular hitbox según ese tamaño
        self.hitbox_width  = int(self.sprite_size[0] * 0.5)
        self.hitbox_height = int(self.sprite_size[1] * 0.25)
        self.hitbox_offset_x = (self.sprite_size[0] - self.hitbox_width) // 2
        self.hitbox_offset_y = self.sprite_size[1] - self.hitbox_height

        # 3️⃣ Tus stats de élite
        self.health     = 100
        self.max_health = 100
        self.speed      = 6.0
