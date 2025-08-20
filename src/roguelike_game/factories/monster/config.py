import json
from typing import Dict, Any
from pathlib import Path
from jsonschema import validate

# Carga de definiciones de monstruos desde JSON
_DATA_DIR = Path(__file__).resolve().parents[4] / "data"
_SCHEMAS_DIR = Path(__file__).resolve().parents[4] / "schemas" / "entities"
with open(_DATA_DIR / "entities/new_monsters.json", encoding="utf-8") as f:
    _monster_cfg = json.load(f)

# Validate against schema
_schema_path = _SCHEMAS_DIR / "NewMonstersSchema.json"
if _schema_path.exists():
    with open(_schema_path, encoding="utf-8") as sf:
        _schema = json.load(sf)
    validate(instance=_monster_cfg, schema=_schema)

# Extract raw monster classes
_raw_classes = _monster_cfg.get("monsters", {}).get("classes", {})

# Flatten stats into top-level and keep assets nested; include optional fsm_set per class
MONSTER_DEFS: Dict[str, Any] = {}
for class_name, class_cfg in _raw_classes.items():
    stats = class_cfg.get("stats", {})
    assets = class_cfg.get("assets", {})
    fsm_set = class_cfg.get("fsm_set")
    patrol = class_cfg.get("patrol")
    MONSTER_DEFS[class_name] = {**stats, "assets": assets, "fsm_set": fsm_set, "patrol": patrol}

# Separate mappings for stats and assets
MONSTER_STATS: Dict[str, Any] = {class_name: class_cfg.get("stats", {}) for class_name, class_cfg in _raw_classes.items()}
MONSTER_ASSETS: Dict[str, Any] = {class_name: class_cfg.get("assets", {}) for class_name, class_cfg in _raw_classes.items()}

# Defaults from file (no hardcoded constants here)
MONSTER_DEFAULTS: Dict[str, Any] = {
    "death_dissapear_time": _monster_cfg.get("DEFAULT_DEATH_DISSAPEAR_TIME")
}

# Dynamic reload of monster definitions

def reload_monster_defs() -> None:
    """
    Reload monster definitions from JSON file.
    """
    import json
    from jsonschema import validate
    global MONSTER_DEFS, MONSTER_STATS, MONSTER_ASSETS, MONSTER_DEFAULTS
    with open(_DATA_DIR / "entities/new_monsters.json", encoding="utf-8") as f:
        monster_cfg = json.load(f)
    # Validate against schema if present
    if _schema_path.exists():
        with open(_schema_path, encoding="utf-8") as sf:
            schema = json.load(sf)
        validate(instance=monster_cfg, schema=schema)
    _raw_classes = monster_cfg.get("monsters", {}).get("classes", {})
    # Flatten stats into top-level and keep assets nested; include optional fsm_set per class
    MONSTER_DEFS.clear()
    for class_name, class_cfg in _raw_classes.items():
        stats = class_cfg.get("stats", {})
        assets = class_cfg.get("assets", {})
        fsm_set = class_cfg.get("fsm_set")
        patrol = class_cfg.get("patrol")
        MONSTER_DEFS[class_name] = {**stats, "assets": assets, "fsm_set": fsm_set, "patrol": patrol}

    # Update stats and assets mappings
    MONSTER_STATS.clear()
    MONSTER_STATS.update({class_name: class_cfg.get("stats", {}) for class_name, class_cfg in _raw_classes.items()})
    MONSTER_ASSETS.clear()
    MONSTER_ASSETS.update({class_name: class_cfg.get("assets", {}) for class_name, class_cfg in _raw_classes.items()})
    MONSTER_DEFAULTS.clear()
    MONSTER_DEFAULTS.update({
        "death_dissapear_time": monster_cfg.get("DEFAULT_DEATH_DISSAPEAR_TIME")
    })