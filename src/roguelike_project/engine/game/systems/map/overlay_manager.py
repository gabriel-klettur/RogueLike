# src.roguelike_project/engine/game/systems/map/overlay_manager.py

import os
import json

"""
Funciones para cargar y guardar la capa overlay de un mapa.
Cada overlay es un array 2D de cadenas de 3 caracteres (o vacías) serializado en JSON.
"""

def load_overlay(map_name: str, base_dir: str = "maps") -> list[list[str]] | None:
    """
    Carga la capa overlay para un mapa dado.
    Retorna None si no existe (implica que aún no hay modificaciones).
    """
    path = os.path.join(base_dir, f"{map_name}.overlay.json")
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_overlay(map_name: str, overlay: list[list[str]], base_dir: str = "maps") -> None:
    """
    Guarda la capa overlay en un archivo JSON.
    """
    os.makedirs(base_dir, exist_ok=True)
    path = os.path.join(base_dir, f"{map_name}.overlay.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(overlay, f, ensure_ascii=False, indent=2)
