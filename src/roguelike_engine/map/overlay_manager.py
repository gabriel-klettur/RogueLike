# src/roguelike_project/engine/game/systems/map/overlay_manager.py
import os
import json
from src.roguelike_project.config import TILES_DATA_PATH

"""
Funciones para cargar y guardar la capa overlay de un mapa.
Cada overlay es un array 2D de cadenas de 3 caracteres (o vacías) serializado en JSON.
"""

def load_overlay(map_name: str) -> list[list[str]] | None:
    """
    Carga la capa overlay para un mapa dado desde TILES_DATA_PATH.
    Retorna None si no existe.
    """
    # Ruta absoluta al archivo overlay
    path = os.path.join(TILES_DATA_PATH, f"{map_name}.overlay.json")
    if not os.path.isfile(path):
        return None
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_overlay(map_name: str, overlay: list[list[str]]) -> None:
    """
    Guarda la capa overlay en TILES_DATA_PATH.
    """
    # Asegurar que exista la carpeta
    os.makedirs(TILES_DATA_PATH, exist_ok=True)
    path = os.path.join(TILES_DATA_PATH, f"{map_name}.overlay.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(overlay, f, ensure_ascii=False, indent=2)
