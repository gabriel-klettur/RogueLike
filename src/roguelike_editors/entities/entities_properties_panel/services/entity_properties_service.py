import os
import json
from typing import Tuple, Any
from roguelike_ui.services.json_persistence import load_from_json


def load_entity_data(ent_id: str, player_stats: dict, monsters: dict) -> Tuple[str, dict, dict]:
    """
    Load the JSON data for the given entity id (player or monster).

    Returns:
        (path, data, entry): absolute json path, classes dict, and the entity entry dict.
    """
    # Decide source file by membership (players vs monsters)
    if ent_id in player_stats:
        path = os.path.join(os.getcwd(), "data", "entities", "new_players.json")
        root = load_from_json(path)
        classes = root.get("players", {}).get("classes", {})
        data = classes
    else:
        path = os.path.join(os.getcwd(), "data", "entities", "new_monsters.json")
        root = load_from_json(path)
        data = root.setdefault("monsters", {}).setdefault("classes", {})

    entry = data.setdefault(ent_id, {})
    return path, data, entry


def save_entity_data(ent_id: str, entry: dict, path: str, player_stats: dict, monsters: dict) -> None:
    """
    Persist the given entity entry into its corresponding JSON file.

    Notes:
    - For players: writes under players.classes[ent_id]
    - For monsters: writes under monsters.classes[ent_id]
    """
    if ent_id in player_stats:
        full = path
        root = load_from_json(full)
        root.setdefault("players", {}).setdefault("classes", {})[ent_id] = entry
        with open(full, "w", encoding="utf-8") as f:
            json.dump(root, f, ensure_ascii=False, indent=2)
    else:
        full = path
        root = load_from_json(full)
        root.setdefault("monsters", {}).setdefault("classes", {})[ent_id] = entry
        with open(full, "w", encoding="utf-8") as f:
            json.dump(root, f, ensure_ascii=False, indent=2)


def convert_value(new_text: str, old_val: Any) -> Any:
    """
    Convert a string input back to the original type of the old value when possible.
    Supported: bool, int, float, str.
    Fallback to string if conversion fails.
    """
    try:
        if isinstance(old_val, bool):
            return new_text.strip().lower() in ("true", "1", "yes", "y", "t")
        try:
            return int(new_text)
        except ValueError:
            pass
        try:
            return float(new_text)
        except ValueError:
            pass
        return new_text
    except Exception:
        return new_text
