import json
import os

def save_to_json(path: str, key: str, value, indent: int = 2):
    """
    Load JSON file, update data[key] = value, and write back to disk.
    """
    full = os.path.abspath(path)
    data = {}
    if os.path.exists(full):
        with open(full, encoding='utf-8-sig') as f:
            data = json.load(f)
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
        with open(full, encoding='utf-8-sig') as f:
            return json.load(f)
    return {}


def remove_from_json(path: str, key: str, indent: int = 2) -> bool:
    """
    Remove key from JSON if present and persist. Returns True if removed.
    """
    full = os.path.abspath(path)
    if not os.path.exists(full):
        return False
    with open(full, encoding='utf-8-sig') as f:
        data = json.load(f)
    if key in data:
        del data[key]
        with open(full, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=indent)
        return True
    return False
