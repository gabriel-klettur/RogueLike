import json
import os

def save_to_json(path: str, key: str, value, indent: int = 2):
    """
    Load JSON file, update data[key] = value, and write back to disk.
    """
    full = os.path.abspath(path)
    data = {}
    if os.path.exists(full):
        try:
            with open(full, encoding='utf-8-sig') as f:
                data = json.load(f)
        except Exception:
            # Si el archivo está vacío o corrupto, empezamos desde un dict vacío
            data = {}
    data[key] = value
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=indent)


def load_from_json(path: str) -> dict:
    """
    Load and return JSON content, or empty dict if missing.
    """
    full = os.path.abspath(path)
    if os.path.exists(full):
        try:
            with open(full, encoding='utf-8-sig') as f:
                content = f.read()
                if content is None:
                    return {}
                if not str(content).strip():
                    return {}
                # Volver al inicio para json.load
                f.seek(0)
                return json.load(f)
        except Exception:
            # En caso de JSON inválido o lectura fallida, devolver dict vacío
            return {}
    return {}


def remove_from_json(path: str, key: str, indent: int = 2) -> bool:
    """
    Remove key from JSON if present and persist. Returns True if removed.
    """
    full = os.path.abspath(path)
    if not os.path.exists(full):
        return False
    try:
        with open(full, encoding='utf-8-sig') as f:
            data = json.load(f)
    except Exception:
        # No se puede cargar: nada que eliminar
        return False
    if key in data:
        del data[key]
        with open(full, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=indent)
        return True
    return False
