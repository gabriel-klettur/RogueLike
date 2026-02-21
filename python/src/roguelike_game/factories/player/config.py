import json
import copy
from pathlib import Path

# Carga de configuración de jugadores en nuevo formato
env_path = Path(__file__).resolve().parents[4] / "data" / "entities" / "new_players.json"
with open(env_path, encoding="utf-8-sig") as f:
    _player_cfg = json.load(f)

# Tamaño original y renderizado de sprites
ORIGINAL_SPRITE_SIZE = tuple(_player_cfg["ORIGINAL_SPRITE_SIZE"])
RENDERED_SPRITE_SIZE = tuple(_player_cfg["RENDERED_SPRITE_SIZE"])

# Extraer clases de jugador
_CLASSES = _player_cfg.get("players", {}).get("classes", {})
PLAYER_STATS = {cls: cfg.get("stats", {}) for cls, cfg in _CLASSES.items()}
PLAYER_ASSETS = {cls: cfg.get("assets", {}) for cls, cfg in _CLASSES.items()}
DEFAULT_CLASS = _player_cfg.get("DEFAULT_CLASS", "")
DEFAULT_SCALE = _player_cfg["DEFAULT_SCALE"]
DEFAULT_SPEED = _player_cfg["DEFAULT_SPEED"]
ANIMATION_INTERVAL = _player_cfg["ANIMATION_INTERVAL"]
INITIAL_ANIMATION_STATE = _player_cfg["INITIAL_ANIMATION_STATE"]
MELEE_WEAPON_CFG = _player_cfg["MELEE_WEAPON"]
DEFAULT_DAMAGE_DURATION = _player_cfg.get("DEFAULT_DAMAGE_DURATION", 0.25)
# Probabilidad por defecto de quedarse quieto al recibir daño (stun) si la clase no la define
DEFAULT_DAMAGE_STOP_PROBABILITY = _player_cfg.get("DEFAULT_DAMAGE_STOP_PROBABILITY", 0.25)
DEFAULT_TRAIL = _player_cfg["DEFAULT_TRAIL"]
FEET_WIDTH_DIVISOR = _player_cfg["FEET_WIDTH_DIVISOR"]
FEET_HEIGHT_DIVISOR = _player_cfg["FEET_HEIGHT_DIVISOR"]