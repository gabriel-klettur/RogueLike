from __future__ import annotations
from typing import Any, Dict
from pathlib import Path
import json


def load_sets(path: str | Path) -> Dict[str, Any]:
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def load_assignments(path: str | Path) -> Dict[str, Any]:
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def load_animation_map(path: str | Path) -> Dict[str, Any]:
    """Load animation_map.json. Returns an object with keys 'default' and optional 'overrides'."""
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def load_layouts(path: str | Path) -> Dict[str, Any]:
    """Load FSM editor graph layouts.
    Structure: {"by_set": {set_id: {"nodes": {node_id: {"x": int, "y": int}}}}}
    """
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)
