# src/roguelike_project/engine/game/entities/player/assets.py
import os
from roguelike_engine.utils.loader import load_image, load_sprite_sheet
from roguelike_engine.config import ASSETS_DIR

def load_character_assets(character_name: str):
    """
    Carga las animaciones 'idle' y 'walk' de un sprite sheet para cada dirección.
    Busca el archivo en ASSETS_DIR/characters/{character_name}/{character_name}{ext}
    """
    directions = ["down", "right", "up", "left"]
    sprite_size = (128, 128)

    # Ruta relativa dentro de assets
    rel_dir = os.path.join('characters', character_name)

    # Buscar con load_image para asegurar que use ASSETS_DIR
    valid_extensions = ['.png', '.PNG', '.webp', '.WEBP']
    found_file = None
    for ext in valid_extensions:
        candidate = os.path.join(rel_dir, f"{character_name}{ext}")
        try:
            # Intentamos cargar sin transformar, para verificar existencia
            load_image(candidate)
            found_file = candidate
            break
        except FileNotFoundError:
            continue

    if not found_file:
        raise FileNotFoundError(f"No se encontró sprite sheet para {character_name} en {rel_dir}")

    # Ahora usamos load_sprite_sheet para obtener frames
    sprites = {}
    for i, direction in enumerate(directions):
        sprites[direction] = {
            'idle': load_sprite_sheet(found_file, sprite_size, row=i, columns=1, start_col=0),
            'walk': load_sprite_sheet(found_file, sprite_size, row=i, columns=4, start_col=1)
        }

    return sprites, sprite_size
