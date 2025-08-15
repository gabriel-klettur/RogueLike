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
    default_animation_map_path,
    load_animation_map,
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
    # animation mapping document
    anim_map: Dict[str, Any]


_CACHE: Optional[_Cached] = None


def _ensure_cache() -> _Cached:
    global _CACHE
    if _CACHE is not None:
        return _CACHE
    sets, assignments = load_all()
    # Load animation map (optional but recommended)
    try:
        anim_map = load_animation_map(default_animation_map_path())
    except Exception as ex:
        logger.warning("[FSMBridge] failed to load animation_map.json: %s", ex)
        anim_map = {"default": {}, "overrides": {}}
    # Basic validation: initial state exists; state classes resolvable
    try:
        _validate_sets(sets)
    except Exception as ex:
        logger.warning("[FSMBridge] validation warnings: %s", ex)
    by_id = {s["id"]: s for s in sets.get("sets", [])}
    _CACHE = _Cached(sets=sets, assignments=assignments, by_id=by_id, anim_map=anim_map)
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
    # Resolve and attach animation map for this set (default + per-set override)
    set_id = set_def.get("id", "") or ""
    try:
        c = _ensure_cache()
        amap_doc = c.anim_map or {}
        default_map = (amap_doc.get("default") or {})
        overrides = ((amap_doc.get("overrides") or {}).get(set_id) or {})
        resolved = dict(default_map)
        resolved.update(overrides)
        fsm.context["anim_map"] = resolved
    except Exception as ex:
        logger.debug("[FSMBridge] anim_map not attached for set_id=%s: %s", set_id, ex)
    # Map state-id -> class-name for editor/runtime helpers (all sets)
    try:
        id_to_class = {s.get("id"): s.get("class") for s in set_def.get("states", []) if s.get("id")}
    except Exception:
        id_to_class = {}
    fsm.context["id_to_class"] = id_to_class
    # Policy for next state after Damage for ANY set:
    # 1) If there is a transition defined in JSON from 'Damage' -> X, use X's class.
    damage_to_class = None
    for tr in set_def.get("transitions", []) or []:
        if tr.get("from") == "Damage":
            to_id = tr.get("to")
            if to_id:
                to_cls = id_to_class.get(to_id)
                if to_cls:
                    damage_to_class = to_cls
                    break
    if damage_to_class:
        fsm.context.setdefault("damage_next_class", damage_to_class)
    # Configure allowed state classes ONLY for Monster_* sets so Player is unaffected.
    if set_id.startswith("Monster_"):
        try:
            allowed_classes = {s.get("class") for s in set_def.get("states", []) if s.get("class")}
        except Exception:
            allowed_classes = set()
        fsm.context["allowed_state_classes"] = allowed_classes
        fsm.context["set_id"] = set_id
        # Always allow transitioning to DeathState by default unless explicitly disabled elsewhere.
        fsm.context.setdefault("allow_death", True)
        # Always allow transitioning to DamageState by default unless explicitly disabled elsewhere.
        fsm.context.setdefault("allow_damage", True)
        # If no explicit Damage transition exists, fall back to a sensible Monster policy.
        if "damage_next_class" not in fsm.context:
            if "AlertChaseState" in allowed_classes:
                fsm.context["damage_next_class"] = "AlertChaseState"
            elif "ChaseState" in allowed_classes:
                fsm.context["damage_next_class"] = "ChaseState"
            else:
                fsm.context["damage_next_class"] = "PatrolState"
    return fsm, initial


def _validate_sets(sets_doc: Dict[str, Any]) -> None:
    """Log warnings for common issues in sets.json."""
    from .fsm_registry import get_state_class
    problems = []
    for s in sets_doc.get("sets", []):
        sid = s.get("id")
        initial = s.get("initial")
        states = {st.get("id"): st for st in s.get("states", [])}
        if not initial or initial not in states:
            problems.append(f"set {sid}: missing/invalid initial '{initial}'")
        for st in states.values():
            cls_name = st.get("class")
            if cls_name and get_state_class(cls_name) is None:
                problems.append(f"set {sid}: unknown state class '{cls_name}'")
    if problems:
        for p in problems:
            logger.warning("[FSMBridge][validate] %s", p)


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
