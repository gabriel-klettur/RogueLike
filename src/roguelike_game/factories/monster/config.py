import json
from pathlib import Path

# Carga de definiciones de monstruos desde JSON
_DATA_DIR = Path(__file__).resolve().parents[4] / "data"
with open(_DATA_DIR / "entities/monsters.json", encoding="utf-8") as f:
    MONSTER_DEFS = json.load(f)

# Dynamic reload of monster definitions

def reload_monster_defs() -> None:
    """
    Reload monster definitions from JSON file.
    """
    import json
    global MONSTER_DEFS
    with open(_DATA_DIR / "entities/monsters.json", encoding="utf-8") as f:
        new_defs = json.load(f)
    MONSTER_DEFS.clear()
    MONSTER_DEFS.update(new_defs)