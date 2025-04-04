from roguelike_project.utils.loader import load_image

def load_character_assets(character_name):
    directions = ["up", "down", "left", "right"]
    sprite_size = (96, 128)
    sprites = {}
    for direction in directions:
        path = f"assets/characters/{character_name}/{character_name}_{direction}.png".lower()
        sprites[direction] = load_image(path, sprite_size)
    return sprites, sprite_size
