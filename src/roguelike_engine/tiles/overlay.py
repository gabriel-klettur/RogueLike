
# Path: src/roguelike_engine/tiles/overlay.py
import os
import json
from roguelike_engine.config.config_tiles import TILES_DATA_PATH


def load_overlay_map(map_name: str) -> list[list[str]] | None:
    """
    Carga la capa overlay desde JSON si existe.
    """
    path = os.path.join(TILES_DATA_PATH, f"{map_name}.overlay.json")
    if not os.path.isfile(path):
        return None

    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_overlay_map(map_name: str, overlay: list[list[str]]) -> None:
    """
    Persiste la capa overlay en formato JSON.
    """
    os.makedirs(TILES_DATA_PATH, exist_ok=True)
    path = os.path.join(TILES_DATA_PATH, f"{map_name}.overlay.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(overlay, f, ensure_ascii=False, indent=2)