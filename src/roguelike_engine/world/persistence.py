# src/roguelike_engine/world/persistence.py

import json
from pathlib import Path
from typing import Any, Dict

def save_world_state(path: str, state: Dict[str, Any]) -> None:
    """
    Guarda el diccionario `state` como JSON en la ruta indicada.
    Crea el directorio si no existe.
    """
    save_path = Path(path)
    save_path.parent.mkdir(parents=True, exist_ok=True)
    with save_path.open("w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=2)

def load_world_state(path: str) -> Dict[str, Any]:
    """
    Lee y devuelve el JSON guardado en la ruta indicada.
    Lanza FileNotFoundError si no existe.
    """
    load_path = Path(path)
    if not load_path.is_file():
        raise FileNotFoundError(f"No se encontró el archivo de estado del mundo: {load_path}")
    with load_path.open("r", encoding="utf-8") as f:
        return json.load(f)
