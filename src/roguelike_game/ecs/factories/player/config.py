import json
from pathlib import Path

# Carga de configuración de jugadores
env_path = Path(__file__).resolve().parents[5] / "data" / "players.json"
with open(env_path, encoding="utf-8") as f:
    _player_cfg = json.load(f)

# Tamaño original y renderizado de sprites
ORIGINAL_SPRITE_SIZE = tuple(_player_cfg["ORIGINAL_SPRITE_SIZE"])
RENDERED_SPRITE_SIZE = tuple(_player_cfg["RENDERED_SPRITE_SIZE"])

# Estadísticas y valores de configuración
PLAYER_STATS = _player_cfg["PLAYER_STATS"]
DEFAULT_CLASS = _player_cfg["DEFAULT_CLASS"]
DEFAULT_SCALE = _player_cfg["DEFAULT_SCALE"]
DEFAULT_SPEED = _player_cfg["DEFAULT_SPEED"]
ANIMATION_INTERVAL = _player_cfg["ANIMATION_INTERVAL"]
INITIAL_ANIMATION_STATE = _player_cfg["INITIAL_ANIMATION_STATE"]
MELEE_WEAPON_CFG = _player_cfg["MELEE_WEAPON"]
DEFAULT_TRAIL = _player_cfg["DEFAULT_TRAIL"]
FEET_WIDTH_DIVISOR = _player_cfg["FEET_WIDTH_DIVISOR"]
FEET_HEIGHT_DIVISOR = _player_cfg["FEET_HEIGHT_DIVISOR"]