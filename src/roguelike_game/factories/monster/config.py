import json
from typing import Dict, Any
from pathlib import Path

# Carga de definiciones de monstruos desde JSON
_DATA_DIR = Path(__file__).resolve().parents[4] / "data"
with open(_DATA_DIR / "entities/new_monsters.json", encoding="utf-8") as f:
    _monster_cfg = json.load(f)

# Extract raw monster classes
_raw_classes = _monster_cfg.get("monsters", {}).get("classes", {})

# Flatten stats into top-level and keep assets nested
MONSTER_DEFS: Dict[str, Any] = {}
for class_name, class_cfg in _raw_classes.items():
    stats = class_cfg.get("stats", {})
    assets = class_cfg.get("assets", {})
    MONSTER_DEFS[class_name] = {**stats, "assets": assets}

# Separate mappings for stats and assets
MONSTER_STATS: Dict[str, Any] = {class_name: class_cfg.get("stats", {}) for class_name, class_cfg in _raw_classes.items()}
MONSTER_ASSETS: Dict[str, Any] = {class_name: class_cfg.get("assets", {}) for class_name, class_cfg in _raw_classes.items()}

# Dynamic reload of monster definitions

def reload_monster_defs() -> None:
    """
    Reload monster definitions from JSON file.
    """
    import json
    global MONSTER_DEFS, MONSTER_STATS, MONSTER_ASSETS
    with open(_DATA_DIR / "entities/new_monsters.json", encoding="utf-8") as f:
        monster_cfg = json.load(f)
    _raw_classes = monster_cfg.get("monsters", {}).get("classes", {})
    # Flatten stats into top-level and keep assets nested
    MONSTER_DEFS.clear()
    for class_name, class_cfg in _raw_classes.items():
        stats = class_cfg.get("stats", {})
        assets = class_cfg.get("assets", {})
        MONSTER_DEFS[class_name] = {**stats, "assets": assets}
    # Update stats and assets mappings
    MONSTER_STATS.clear()
    MONSTER_STATS.update({class_name: class_cfg.get("stats", {}) for class_name, class_cfg in _raw_classes.items()})
    MONSTER_ASSETS.clear()
    MONSTER_ASSETS.update({class_name: class_cfg.get("assets", {}) for class_name, class_cfg in _raw_classes.items()})