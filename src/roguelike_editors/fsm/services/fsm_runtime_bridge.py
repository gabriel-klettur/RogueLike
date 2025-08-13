"""Runtime bridge for FSM sets (skeleton).

- build_snapshot: transform editor JSON into runtime-friendly structure
- publish_reload: notify game systems of a reload/version bump
"""
from __future__ import annotations
from typing import Any, Dict


FSM_SETS_VERSION: int = 0


def build_snapshot(editor_data: Dict[str, Any]) -> Dict[str, Any]:
    # TODO: preindex and freeze structures
    return editor_data


def publish_reload() -> int:
    global FSM_SETS_VERSION
    FSM_SETS_VERSION += 1
    # TODO: emit an engine-wide event (e.g., via existing event bus)
    return FSM_SETS_VERSION


__all__ = ["FSM_SETS_VERSION", "build_snapshot", "publish_reload"]
