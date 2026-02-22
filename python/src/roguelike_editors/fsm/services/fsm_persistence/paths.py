from __future__ import annotations
from pathlib import Path


def _project_root() -> Path:
    """Resolve the project root by locating the 'src' directory and returning its parent.
    Fallback to a conservative parent traversal if not found.
    """
    here = Path(__file__).resolve()
    for p in here.parents:
        if p.name == 'src':
            return p.parent
    try:
        # Fallback: .../RogueLike/src/roguelike_editors/fsm/services
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


def default_ids_path() -> Path:
    """Path for exported FSM ids index JSON.
    Structure: {"SET_IDS": [...], "STATES_BY_SET": {...}, "TRANSITIONS_BY_SET": {...}}
    """
    return _project_root() / "data" / "fsm" / "fsm_ids.json"
