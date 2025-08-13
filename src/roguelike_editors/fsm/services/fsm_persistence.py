"""Persistence layer for FSM Sets (skeleton).

- load_sets / save_sets to data/fsm/sets.json
- validate against data/fsm/schema.json
"""
from __future__ import annotations
from typing import Any, Dict


def load_sets(path: str) -> Dict[str, Any]:
    """Load FSM sets from JSON file. TODO: implement fully."""
    import json
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_sets(data: Dict[str, Any], path: str) -> None:
    """Save FSM sets to JSON file (pretty, deterministic)."""
    import json
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)


def validate(data: Dict[str, Any], schema_path: str) -> None:
    """Validate data with JSON Schema if 'jsonschema' is available.
    Raise ValueError on validation errors. No-op if schema not found.
    """
    try:
        import json
        import jsonschema  # type: ignore
        with open(schema_path, "r", encoding="utf-8") as f:
            schema = json.load(f)
        jsonschema.validate(instance=data, schema=schema)
    except FileNotFoundError:
        # Schema optional during early development
        return
    except ImportError:
        # Validation optional if dependency not present
        return


__all__ = ["load_sets", "save_sets", "validate"]
