from roguelike_engine.utils.loader import load_sprite_sheet
from roguelike_game.config.players_config import PLAYER_ASSETS

class PlayerAssets:
    """
    Encapsula la carga de sprites desde un sprite sheet único.
    """
    def __init__(self, class_player: str, sprite_size: tuple[int, int]):
        self.class_player = class_player
        self.sprite_size = sprite_size
        # Ruta a los assets definida en players.json
        assets_entry = PLAYER_ASSETS.get(class_player)
        if isinstance(assets_entry, str):
            self.sheet_path = assets_entry
        elif isinstance(assets_entry, dict):
            # por defecto usamos la variante walking
            self.sheet_path = assets_entry.get("walking")
        else:
            raise KeyError(f"No asset configurado para clase {class_player}")           

    def get_sprites(self) -> tuple[dict[str, dict[str, list]], tuple[int, int]]:
        """
        Devuelve un dict:
            {
              'up':    {'idle': [...], 'walk': [...]},
              'down':  {...},
              ...
            }
        junto con el tamaño de cada sprite.
        """
        # Determinar grid o strip según configuración
        assets_entry = PLAYER_ASSETS.get(self.class_player)
        sprites: dict[str, dict[str, list]] = {}
        if isinstance(assets_entry, str):
            # grid 4x5 (dwarf)
            directions = ['down', 'right', 'up', 'left']
            for row_idx, direction in enumerate(directions):
                # idle: 1 frame en col 0
                idle_frames = load_sprite_sheet(
                    self.sheet_path,
                    self.sprite_size,
                    row=row_idx,
                    columns=1,
                    start_col=0
                )
                # walk: 4 frames cols 1-4
                walk_frames = load_sprite_sheet(
                    self.sheet_path,
                    self.sprite_size,
                    row=row_idx,
                    columns=4,
                    start_col=1
                )
                sprites[direction] = {'idle': idle_frames, 'walk': walk_frames}
        elif isinstance(assets_entry, dict):
            # strip 1x40 (otros)
            directions = ['down', 'down_right', 'right', 'up_right', 'up', 'up_left', 'left', 'down_left']
            # cada bloque de 5 columnas para las 8 direcciones: down, down_right, right, up_right, up, up_left, left, down_left
            block_indices = list(range(8))
            for direction, block in zip(directions, block_indices):
                frames = load_sprite_sheet(
                    self.sheet_path,
                    self.sprite_size,
                    row=0,
                    columns=5,


                    
                    start_col=block * 5
                )
                # primera frame idle, resto walk
                idle = [frames[0]]
                walk = frames[1:]
                sprites[direction] = {'idle': idle, 'walk': walk}
        else:
            raise KeyError(f"No asset configurado para clase {self.class_player}")
        return sprites, self.sprite_size
