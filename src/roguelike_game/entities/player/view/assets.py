"""
Carga de sprites y assets para el jugador.
"""
# Path: src/roguelike_game/entities/player/view/assets.py
import os
from roguelike_engine.utils.loader import load_sprite_sheet

"""
Carga de sprites y assets para el jugador.
"""
import os
from roguelike_engine.utils.loader import load_sprite_sheet

class PlayerAssets:
    """
    Encapsula la carga de sprites desde un sprite sheet único.
    """
    def __init__(self, character_name: str, sprite_size: tuple[int,int]):
        self.character_name = character_name
        self.sprite_size = sprite_size
        # Ruta relativa dentro de ASSETS_DIR
        self.sheet_path = f"characters/{character_name}/{character_name}.png"

    def get_sprites(self) -> tuple[dict[str,dict[str,list]], tuple[int,int]]:
        """
        Devuelve un dict:
            {
              'up':    {'idle': [...], 'walk': [...]},
              'down':  {...},
              ...
            }
        junto con el tamaño de cada sprite.
        """
        directions = ['down', 'right', 'up', 'left']
        sprites = {}
        for row_idx, direction in enumerate(directions):
            # Idle: 1 frame en la columna 0
            idle_frames = load_sprite_sheet(
                self.sheet_path,
                self.sprite_size,
                row=row_idx,
                columns=1,
                start_col=0
            )
            # Walk: 4 frames comenzando en columna 1
            walk_frames = load_sprite_sheet(
                self.sheet_path,
                self.sprite_size,
                row=row_idx,
                columns=4,
                start_col=1
            )
            sprites[direction] = {
                'idle': idle_frames,
                'walk': walk_frames
            }
        return sprites, self.sprite_size