"""Runtime bridge for FSM sets.

- Carga y cachea sets y assignments desde data/fsm
- Construye una FiniteStateMachine desde un set usando el registry de estados
- Expone utilidades para builders (player, monster)
"""
from __future__ import annotations
from typing import Any, Dict, Optional, Tuple
import logging

from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine

# Delegated services (thin facade)
from .fsm_cache import (
    ensure_cache,
    clear_cache,
    get_snapshot as _cache_get_snapshot,
    get_set as _cache_get_set,
)
from .fsm_linter import lint_set_params as _lint_set_params
from .fsm_highlight import (
    set_editor_highlight_set as _set_highlight_set,
    get_editor_highlight_set as _get_highlight_set,
    set_editor_highlight_context as _set_highlight_ctx,
    get_editor_highlight_context as _get_highlight_ctx,
)
from .fsm_assignments import assignment_for
from .fsm_builder import build_fsm_from_set as _build_fsm_from_set


logger = logging.getLogger(__name__)


FSM_SETS_VERSION: int = 0


def reload() -> int:
    """Reload sets/assignments from disk and bump version."""
    clear_cache()
    return publish_reload()


def set_editor_highlight_set(set_id: Optional[str]) -> None:
    """Optional: store a set_id for the Editor UI to highlight in the Sets panel.
    Pass None to clear highlight.
    """
    _set_highlight_set(set_id)


def get_editor_highlight_set() -> Optional[str]:
    """Return the currently highlighted set id, if any."""
    return _get_highlight_set()


def set_editor_highlight_context(set_id: Optional[str], params: Optional[Dict[str, Any]]) -> None:
    """Set highlight context (set_id + params) for Editor panels to reflect hover and lint.
    Pass None to clear.
    """
    _set_highlight_ctx(set_id, params)


def get_editor_highlight_context() -> Tuple[Optional[str], Optional[Dict[str, Any]]]:
    """Return (set_id, params) of the current highlight context, if any."""
    return _get_highlight_ctx()


def lint_set_params(set_id: str, params: Optional[Dict[str, Any]]) -> list:
    """Lightweight linter for Spawner_* set params. Returns a list of warning strings.
    Rules are intentionally simple and non-fatal.
    """
    return _lint_set_params(set_id, params)


def get_snapshot() -> Dict[str, Any]:
    """Return editor snapshot (raw sets JSON)."""
    return _cache_get_snapshot()


def get_set(set_id: str) -> Optional[Dict[str, Any]]:
    return _cache_get_set(set_id)


def build_fsm_from_set(set_def: Dict[str, Any]) -> Tuple[FiniteStateMachine, str]:
    """Create a FiniteStateMachine from a set definition. Returns (fsm, initial_state_name).

    For now, we instantiate only the initial state's class and rely on coded transitions
    inside each State.execute().
    """
    return _build_fsm_from_set(set_def)


def get_ids_index() -> Dict[str, Any]:
    """Return the cached ids index JSON for tooling/runtime.
    Structure: {"SET_IDS": [...], "STATES_BY_SET": {...}, "TRANSITIONS_BY_SET": {...}}
    """
    return ensure_cache().ids_index


def get_set_ids() -> list:
    """Return list of set ids (ordered as in JSON export)."""
    try:
        return list(ensure_cache().ids_index.get("SET_IDS", []) or [])
    except Exception:
        return []


def get_state_ids(set_id: str) -> list:
    """Return list of state ids for a set id."""
    try:
        return list((ensure_cache().ids_index.get("STATES_BY_SET", {}) or {}).get(set_id, []) or [])
    except Exception:
        return []


def get_transition_ids(set_id: str) -> list:
    """Return list of transition ids for a set id."""
    try:
        return list((ensure_cache().ids_index.get("TRANSITIONS_BY_SET", {}) or {}).get(set_id, []) or [])
    except Exception:
        return []


def build_fsm_for_archetype(archetype: str, *, eid: Optional[int] = None) -> Optional[Tuple[FiniteStateMachine, str]]:
    """Return (fsm, initial_name) for an archetype (e.g., 'player', 'goblin'), or None if no assignment."""
    set_id = assignment_for(archetype, eid=eid)
    if not set_id:
        logger.debug("[FSMBridge] no assignment for archetype=%s", archetype)
        return None
    set_def = get_set(set_id)
    if not set_def:
        logger.warning("[FSMBridge] assignment references missing set_id=%s", set_id)
        return None
    try:
        fsm, initial = build_fsm_from_set(set_def)
        return fsm, initial
    except Exception as ex:
        logger.exception("[FSMBridge] failed to build FSM for set_id=%s: %s", set_id, ex)
        return None


def publish_reload() -> int:
    global FSM_SETS_VERSION
    FSM_SETS_VERSION += 1
    # TODO: emit an engine-wide event (e.g., via existing event bus)
    return FSM_SETS_VERSION


__all__ = [
    "FSM_SETS_VERSION",
    "reload",
    "get_snapshot",
    "get_set",
    "build_fsm_from_set",
    "build_fsm_for_archetype",
    "publish_reload",
    "set_editor_highlight_set",
    "get_editor_highlight_set",
    "set_editor_highlight_context",
    "get_editor_highlight_context",
    "lint_set_params",
    "get_ids_index",
    "get_set_ids",
    "get_state_ids",
    "get_transition_ids",
]
