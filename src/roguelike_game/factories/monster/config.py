import json
from typing import Dict, Any
from pathlib import Path
from jsonschema import validate

# Carga de definiciones de monstruos desde JSON
_DATA_DIR = Path(__file__).resolve().parents[4] / "data"
_SCHEMAS_DIR = Path(__file__).resolve().parents[4] / "schemas" / "entities"
with open(_DATA_DIR / "entities/new_hostiles.json", encoding="utf-8-sig") as f:
    _monster_cfg = json.load(f)
    # Try to also load neutrals and merge their classes into a combined view
    _neutrals_path = _DATA_DIR / "entities/new_neutrals.json"
    _neutrals_cfg = None
    if _neutrals_path.exists():
        try:
            with open(_neutrals_path, encoding="utf-8-sig") as nf:
                _neutrals_cfg = json.load(nf)
        except Exception:
            _neutrals_cfg = None

# Validate against schema
_schema_path = _SCHEMAS_DIR / "NewHostilesSchema.json"
if _schema_path.exists():
    with open(_schema_path, encoding="utf-8-sig") as sf:
        _schema = json.load(sf)
    validate(instance=_monster_cfg, schema=_schema)

# Extract raw classes from hostiles and neutrals (if present) and merge
_raw_classes: Dict[str, Any] = {}
_raw_classes.update(_monster_cfg.get("hostiles", {}).get("classes", {}))
if '_neutrals_cfg' in locals() and _neutrals_cfg:
    _raw_classes.update(_neutrals_cfg.get("neutrals", {}).get("classes", {}))

# Flatten stats into top-level and keep assets nested; include optional fsm_set per class
MONSTER_DEFS: Dict[str, Any] = {}
for class_name, class_cfg in _raw_classes.items():
    stats = class_cfg.get("stats", {})
    assets = class_cfg.get("assets", {})
    fsm_set = class_cfg.get("fsm_set")
    patrol = class_cfg.get("patrol")
    default_name = class_cfg.get("default_name")
    MONSTER_DEFS[class_name] = {**stats, "assets": assets, "fsm_set": fsm_set, "patrol": patrol, "default_name": default_name}

# Separate mappings for stats and assets
MONSTER_STATS: Dict[str, Any] = {class_name: class_cfg.get("stats", {}) for class_name, class_cfg in _raw_classes.items()}
MONSTER_ASSETS: Dict[str, Any] = {class_name: class_cfg.get("assets", {}) for class_name, class_cfg in _raw_classes.items()}

# Defaults from file (no hardcoded constants here)
MONSTER_DEFAULTS: Dict[str, Any] = {
    "death_dissapear_time": _monster_cfg.get("DEFAULT_DEATH_DISSAPEAR_TIME"),
    # Probabilidad por defecto de quedarse quieto al recibir daño
    "damage_stop_probability": _monster_cfg.get("DEFAULT_DAMAGE_STOP_PROBABILITY", 0.25),
}

# Dynamic reload of monster definitions

def reload_monster_defs() -> None:
    """
    Reload monster definitions from JSON file.
    """
    import json
    from jsonschema import validate
    global MONSTER_DEFS, MONSTER_STATS, MONSTER_ASSETS, MONSTER_DEFAULTS
    with open(_DATA_DIR / "entities/new_hostiles.json", encoding="utf-8-sig") as f:
        monster_cfg = json.load(f)
    neutrals_cfg = None
    neutrals_path = _DATA_DIR / "entities/new_neutrals.json"
    if neutrals_path.exists():
        try:
            with open(neutrals_path, encoding="utf-8-sig") as nf:
                neutrals_cfg = json.load(nf)
        except Exception:
            neutrals_cfg = None
    # Validate against schema if present
    if _schema_path.exists():
        with open(_schema_path, encoding="utf-8-sig") as sf:
            schema = json.load(sf)
        validate(instance=monster_cfg, schema=schema)
    _raw_classes: Dict[str, Any] = {}
    _raw_classes.update(monster_cfg.get("hostiles", {}).get("classes", {}))
    if neutrals_cfg:
        _raw_classes.update(neutrals_cfg.get("neutrals", {}).get("classes", {}))
    # Flatten stats into top-level and keep assets nested; include optional fsm_set per class
    MONSTER_DEFS.clear()
    for class_name, class_cfg in _raw_classes.items():
        stats = class_cfg.get("stats", {})
        assets = class_cfg.get("assets", {})
        fsm_set = class_cfg.get("fsm_set")
        patrol = class_cfg.get("patrol")
        default_name = class_cfg.get("default_name")
        MONSTER_DEFS[class_name] = {**stats, "assets": assets, "fsm_set": fsm_set, "patrol": patrol, "default_name": default_name}

    # Update stats and assets mappings
    MONSTER_STATS.clear()
    MONSTER_STATS.update({class_name: class_cfg.get("stats", {}) for class_name, class_cfg in _raw_classes.items()})
    MONSTER_ASSETS.clear()
    MONSTER_ASSETS.update({class_name: class_cfg.get("assets", {}) for class_name, class_cfg in _raw_classes.items()})
    MONSTER_DEFAULTS.clear()
    MONSTER_DEFAULTS.update({
        "death_dissapear_time": monster_cfg.get("DEFAULT_DEATH_DISSAPEAR_TIME"),
        "damage_stop_probability": monster_cfg.get("DEFAULT_DAMAGE_STOP_PROBABILITY", 0.25),
    })