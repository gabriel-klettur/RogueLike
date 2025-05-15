# src/roguelike_game/entities/npc/utils/stats_loader.py

# Path: src/roguelike_game/entities/npc/utils/stats_loader.py
import yaml
from pathlib import Path
from typing import Dict

def load_npc_stats(config_path: str) -> Dict[str, float]:
    """
    Lee un archivo YAML con claves como:
      health: 60
      max_health: 60
      speed: 5.0
    y devuelve un dict con esos valores.
    """
    path = Path(config_path)
    if not path.is_file():
        raise FileNotFoundError(f"Config no encontrada: {config_path}")

    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f)

    # Validaciones mínimas
    required = ["health", "max_health", "speed"]
    for key in required:
        if key not in data:
            raise KeyError(f"Falta '{key}' en {config_path}")

    return data