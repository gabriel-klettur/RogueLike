from roguelike_engine.utils.loader import load_sprite_sheet, load_image
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
        
        # Nuevo formato: 'sets' con sprite sheets por estado
        if isinstance(assets_entry, dict) and 'sets' in assets_entry:
            sprites: dict[str, dict[str, list]] = {}
            # Direcciones básicas (8 direcciones)
            directions = ['down','down_right','right','up_right','up','up_left','left','down_left']
            # Inicializar mapa de estados por dirección
            for direction in directions:
                sprites[direction] = {}
            # Procesar cada estado de animación
            for state, paths in assets_entry['sets'].get('sprites_set', {}).items():
                if not paths:
                    continue
                key = 'walk' if state == 'walking' else state
                sheet_path = paths[0]
                # Determinar columnas a partir del ancho del sheet
                # Eliminamos detección dinámica de columnas; siempre 5 frames por dirección
                # Cortar frames para cada dirección
                for direction, block in zip(directions, range(len(directions))):
                    frames = load_sprite_sheet(
                        sheet_path,
                        self.sprite_size,
                        row=0,
                        columns=(5 if key in ('walk','idle') else 1),
                        start_col=block*5
                    )
                    sprites[direction][key] = frames
            return sprites, self.sprite_size
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
