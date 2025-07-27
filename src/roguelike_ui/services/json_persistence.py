import json
import os

def save_to_json(path: str, key: str, value, indent: int = 2):
    """
    Load JSON file, update data[key] = value, and write back to disk.
    """
    full = os.path.abspath(path)
    data = {}
    if os.path.exists(full):
        with open(full, encoding='utf-8') as f:
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
        with open(full, encoding='utf-8') as f:
            return json.load(f)
    return {}
