import json
from pathlib import Path

# Script para transformar new_monsters.json al formato estándar (similar a new_players.json)
ROOT = Path(__file__).resolve().parent
DATA_DIR = ROOT.parent / "data" / "entities"
INPUT = DATA_DIR / "new_monsters.json"

with open(INPUT, 'r', encoding='utf-8') as f:
    old = json.load(f)

# Construir nueva estructura
new = {
    "ORIGINAL_SPRITE_SIZE": old.get("ORIGINAL_SPRITE_SIZE", [128, 128]),
    "RENDERED_SPRITE_SIZE": old.get("RENDERED_SPRITE_SIZE", [64, 64]),
    "DEFAULT_CLASS": next(iter(old)),
    "DEFAULT_SCALE": 1.0,
    "DEFAULT_SPEED": 5,
    "DEFAULT_TRAIL": {"interval": 0.1, "life_time": 0.5, "max_trails": 10},
    "FEET_WIDTH_DIVISOR": 2,
    "FEET_HEIGHT_DIVISOR": 4,
    "ANIMATION_INTERVAL": 0.15,
    "INITIAL_ANIMATION_STATE": "down_idle",
    "MELEE_WEAPON": {"damage": 1, "cooldown": 1.0},
    # Mantener todas las clases de monstruo bajo "monsters" -> "classes"
    "monsters": {"classes": old}
}

# Sobrescribir el JSON
with open(INPUT, 'w', encoding='utf-8') as f:
    json.dump(new, f, indent=2)

print(f"Transformado new_monsters.json: {INPUT}")
