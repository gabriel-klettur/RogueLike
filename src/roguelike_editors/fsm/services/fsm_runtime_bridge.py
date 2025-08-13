"""Runtime bridge for FSM sets.

- Carga y cachea sets y assignments desde data/fsm
- Construye una FiniteStateMachine desde un set usando el registry de estados
- Expone utilidades para builders (player, monster)
"""
from __future__ import annotations
from typing import Any, Dict, Optional, Tuple
from dataclasses import dataclass

from .fsm_persistence import (
    load_all,
    default_sets_path,
    default_assignments_path,
)
from .fsm_registry import get_state_class
from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine

import logging
logger = logging.getLogger(__name__)


FSM_SETS_VERSION: int = 0


@dataclass
class _Cached:
    sets: Dict[str, Any]
    assignments: Dict[str, Any]
    # fast lookup by id
    by_id: Dict[str, Dict[str, Any]]


_CACHE: Optional[_Cached] = None


def _ensure_cache() -> _Cached:
    global _CACHE
    if _CACHE is not None:
        return _CACHE
    sets, assignments = load_all()
    by_id = {s["id"]: s for s in sets.get("sets", [])}
    _CACHE = _Cached(sets=sets, assignments=assignments, by_id=by_id)
    logger.debug("[FSMBridge] cache loaded: %d sets", len(by_id))
    return _CACHE


def reload() -> int:
    """Reload sets/assignments from disk and bump version."""
    global _CACHE
    _CACHE = None
    return publish_reload()


def get_snapshot() -> Dict[str, Any]:
    """Return editor snapshot (raw sets JSON)."""
    c = _ensure_cache()
    return c.sets


def get_set(set_id: str) -> Optional[Dict[str, Any]]:
    return _ensure_cache().by_id.get(set_id)


def build_fsm_from_set(set_def: Dict[str, Any]) -> Tuple[FiniteStateMachine, str]:
    """Create a FiniteStateMachine from a set definition. Returns (fsm, initial_state_name).

    For now, we instantiate only the initial state's class and rely on coded transitions
    inside each State.execute().
    """
    initial = set_def.get("initial")
    states = {s["id"]: s for s in set_def.get("states", [])}
    initial_def = states.get(initial)
    if not initial_def:
        raise ValueError(f"FSM set '{set_def.get('id')}' missing initial state '{initial}'")
    class_name = initial_def.get("class")
    cls = get_state_class(class_name) if class_name else None
    if cls is None:
        raise ValueError(f"FSM state class not found: {class_name}")
    fsm = FiniteStateMachine(cls())
    return fsm, initial


def _assignment_for(archetype: str, eid: Optional[int] = None) -> Optional[str]:
    c = _ensure_cache()
    by_eid = c.assignments.get("by_eid", {})
    if eid is not None and str(eid) in by_eid:
        return by_eid[str(eid)]
    return c.assignments.get("by_archetype", {}).get(archetype)


def build_fsm_for_archetype(archetype: str, *, eid: Optional[int] = None) -> Optional[Tuple[FiniteStateMachine, str]]:
    """Return (fsm, initial_name) for an archetype (e.g., 'player', 'goblin'), or None if no assignment."""
    set_id = _assignment_for(archetype, eid=eid)
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
]
