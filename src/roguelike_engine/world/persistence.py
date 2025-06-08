# src/roguelike_engine/world/persistence.py

from pathlib import Path
from typing import Any, Dict

# Usa orjson si está disponible para acelerar parsing
try:
    import orjson as _json
    _USE_ORJSON = True
except ImportError:
    import json as _json
    _USE_ORJSON = False

def save_world_state(path: str, state: Dict[str, Any]) -> None:
    """
    Guarda el diccionario `state` como JSON en la ruta indicada.
    Crea el directorio si no existe.
    """
    save_path = Path(path)
    save_path.parent.mkdir(parents=True, exist_ok=True)
    # Guarda JSON; usa _json (orjson/json)
    if _USE_ORJSON:
        # orjson.dumps retorna bytes
        content = _json.dumps(state)
        save_path.write_bytes(content)
    else:
        with save_path.open("w", encoding="utf-8") as f:
            _json.dump(state, f, ensure_ascii=False, indent=2)

def load_world_state(path: str) -> Dict[str, Any]:
    """
    Lee y devuelve el JSON guardado en la ruta indicada.
    Lanza FileNotFoundError si no existe.
    """
    load_path = Path(path)
    if not load_path.is_file():
        raise FileNotFoundError(f"No se encontró el archivo de estado del mundo: {load_path}")
    # Carga JSON de estado; usa _json (orjson/json) para parsear
    raw = load_path.read_bytes()
    return _json.loads(raw)
