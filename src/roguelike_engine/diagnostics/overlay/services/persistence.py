from __future__ import annotations

from typing import Iterable, List, Optional
import os
import json


def get_state_file_path(base_path: Optional[str] = None) -> str:
    """Resolve the JSON state file path for diagnostics overlay collapsed groups.

    If base_path is None, it infers the project root by walking up from this file
    to reach the repository root (four levels up from this module path), then uses
    data/diagnostics/overlay_state.json under that root.
    """
    if base_path is None:
        here = os.path.abspath(os.path.dirname(__file__))
        # services -> overlay -> diagnostics -> roguelike_engine -> src -> RogueLike (project root)
        project_root = os.path.abspath(os.path.join(here, "..", "..", "..", "..", ".."))
    else:
        project_root = os.path.abspath(base_path)
    path = os.path.join(project_root, "data", "diagnostics")
    os.makedirs(path, exist_ok=True)
    return os.path.join(path, "overlay_state.json")


def load_overlay_state(base_path: Optional[str] = None) -> List[str]:
    """Load collapsed group ids from disk. Returns a list (may be empty)."""
    try:
        fp = get_state_file_path(base_path)
        if os.path.exists(fp):
            with open(fp, "r", encoding="utf-8") as f:
                data = json.load(f)
            cols = data.get("collapsed_groups", [])
            if isinstance(cols, list):
                return cols
    except Exception:
        # Fail silently; diagnostics overlay should not crash the game
        pass
    return []


def save_overlay_state(collapsed_groups: Iterable[str], base_path: Optional[str] = None) -> None:
    """Persist collapsed group ids to disk (sorted for stability)."""
    try:
        fp = get_state_file_path(base_path)
        data = {"collapsed_groups": sorted(list(collapsed_groups))}
        with open(fp, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
    except Exception:
        # Fail silently to avoid impacting runtime
        pass
