import os
from roguelike_project.utils.loader import load_sprite_sheet

def load_character_assets(character_name):
    directions = ["down", "right", "up", "left"]
    sprite_size = (128, 128)

    base_dir = os.path.join("roguelike_project", "assets", "characters", character_name)
    valid_extensions = [".png", ".PNG", ".webp", ".WEBP"]
    found_path = None

    for ext in valid_extensions:
        test_path = os.path.join(base_dir, f"{character_name}{ext}")
        if os.path.exists(test_path):
            found_path = test_path
            break

    if not found_path:
        raise FileNotFoundError(f"No se encontró sprite sheet para {character_name} en {base_dir}")

    sprites = {}
    for i, direction in enumerate(directions):
        sprites[direction] = {
            "idle": load_sprite_sheet(found_path, sprite_size, row=i, columns=1, start_col=0),
            "walk": load_sprite_sheet(found_path, sprite_size, row=i, columns=4, start_col=1)
        }

    return sprites, sprite_size
