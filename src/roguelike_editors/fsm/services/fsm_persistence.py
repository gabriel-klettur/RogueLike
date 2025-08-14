"""Persistence layer for FSM Sets and Assignments.

- load_sets / save_sets to data/fsm/sets.json
- load_assignments / save_assignments to data/fsm/assignments.json
- validate against data/fsm/schema.json (optional)
"""
from __future__ import annotations
from typing import Any, Dict, Tuple
from pathlib import Path


def _project_root() -> Path:
    """Resolve the project root by locating the 'src' directory and returning its parent."""
    here = Path(__file__).resolve()
    for p in here.parents:
        if p.name == 'src':
            return p.parent
    # Fallback: assume standard depth .../RogueLike/src/roguelike_editors/fsm/services
    try:
        return here.parents[4]
    except Exception:
        return here.parent


def default_sets_path() -> Path:
    return _project_root() / "data" / "fsm" / "sets.json"


def default_schema_path() -> Path:
    return _project_root() / "data" / "fsm" / "schema.json"


def default_assignments_path() -> Path:
    return _project_root() / "data" / "fsm" / "assignments.json"


def default_layouts_path() -> Path:
    """Path for FSM editor graph layouts (node positions per set)."""
    return _project_root() / "data" / "fsm" / "layouts.json"


def default_animation_map_path() -> Path:
    """Path for FSM animation map (state-class -> animation base, with per-set overrides)."""
    return _project_root() / "data" / "fsm" / "animation_map.json"


def load_sets(path: str | Path) -> Dict[str, Any]:
    """Load FSM sets from JSON file. TODO: implement fully."""
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def save_sets(data: Dict[str, Any], path: str | Path) -> None:
    """Save FSM sets to JSON file (pretty, deterministic)."""
    import json
    with open(str(path), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)


def load_assignments(path: str | Path) -> Dict[str, Any]:
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def save_assignments(data: Dict[str, Any], path: str | Path) -> None:
    import json
    with open(str(path), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)


def load_animation_map(path: str | Path) -> Dict[str, Any]:
    """Load animation_map.json. Returns an object with keys 'default' and optional 'overrides'."""
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def load_layouts(path: str | Path) -> Dict[str, Any]:
    """Load FSM editor graph layouts.
    Structure: {"by_set": {set_id: {"nodes": {node_id: {"x": int, "y": int}}}}}
    """
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def save_layouts(data: Dict[str, Any], path: str | Path) -> None:
    """Save FSM editor graph layouts (pretty)."""
    import json
    # Ensure parent exists
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(str(p), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)


def validate(data: Dict[str, Any], schema_path: str | Path) -> None:
    """Validate data with JSON Schema if 'jsonschema' is available.
    Raise ValueError on validation errors. No-op if schema not found.
    """
    try:
        import json
        import jsonschema  # type: ignore
        with open(str(schema_path), "r", encoding="utf-8") as f:
            schema = json.load(f)
        jsonschema.validate(instance=data, schema=schema)
    except FileNotFoundError:
        # Schema optional during early development
        return
    except ImportError:
        # Validation optional if dependency not present
        return


def load_all(
    sets_path: Path | None = None,
    assignments_path: Path | None = None,
    schema_path: Path | None = None,
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    """Load sets and assignments with optional validation. Returns (sets, assignments)."""
    sets_path = sets_path or default_sets_path()
    assignments_path = assignments_path or default_assignments_path()
    schema_path = schema_path or default_schema_path()

    sets = load_sets(sets_path)
    try:
        validate(sets, schema_path)
    except Exception:
        # Keep running even if schema invalid or not present
        pass
    try:
        assignments = load_assignments(assignments_path)
    except FileNotFoundError:
        assignments = {"by_archetype": {}, "by_eid": {}}
    return sets, assignments


__all__ = [
    "default_sets_path",
    "default_assignments_path",
    "default_layouts_path",
    "default_schema_path",
    "default_animation_map_path",
    "load_sets",
    "save_sets",
    "load_assignments",
    "save_assignments",
    "load_animation_map",
    "load_layouts",
    "save_layouts",
    "validate",
    "load_all",
]
