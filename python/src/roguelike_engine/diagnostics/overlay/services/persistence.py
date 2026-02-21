from __future__ import annotations

from typing import Iterable, List, Optional, Dict
import os
import json
import tempfile
import io


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


def load_overlay_ui_state(base_path: Optional[str] = None) -> Dict:
    try:
        fp = get_state_file_path(base_path)
        if os.path.exists(fp):
            with open(fp, "r", encoding="utf-8") as f:
                data = json.load(f)
            ui = data.get("ui", {})
            if isinstance(ui, dict):
                return ui
    except Exception:
        pass
    return {}


def save_overlay_state(collapsed_groups: Iterable[str], base_path: Optional[str] = None, ui: Optional[Dict] = None) -> None:
    try:
        fp = get_state_file_path(base_path)
        existing = {}
        if os.path.exists(fp):
            try:
                with open(fp, "r", encoding="utf-8") as rf:
                    existing = json.load(rf) or {}
            except Exception:
                existing = {}
        data = dict(existing)
        data["collapsed_groups"] = sorted(list(collapsed_groups))
        if ui is not None and isinstance(ui, dict):
            data["ui"] = dict(ui)
        # Atomic write: write to a temp file in the same directory, then replace.
        target_dir = os.path.dirname(fp)
        os.makedirs(target_dir, exist_ok=True)
        fd, tmp_path = tempfile.mkstemp(prefix="overlay_state_", suffix=".json.tmp", dir=target_dir)
        try:
            with io.open(fd, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
                f.flush()
                os.fsync(f.fileno())
            os.replace(tmp_path, fp)
        finally:
            # If replace succeeded, tmp_path no longer exists. If it failed, ensure cleanup.
            try:
                if os.path.exists(tmp_path):
                    os.remove(tmp_path)
            except Exception:
                pass
    except Exception:
        # Fail silently to avoid impacting runtime
        pass
