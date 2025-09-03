"""Runtime bridge for FSM sets.

- Carga y cachea sets y assignments desde data/fsm
- Construye una FiniteStateMachine desde un set usando el registry de estados
- Expone utilidades para builders (player, monster)
"""
from __future__ import annotations
from typing import Any, Dict, Optional, Tuple
from dataclasses import dataclass, field

from .fsm_persistence import (
    load_all,
    default_sets_path,
    default_assignments_path,
    default_animation_map_path,
    default_ids_path,
    load_animation_map,
)
from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine

import json
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
    # ids index JSON (SET_IDS/STATES_BY_SET/TRANSITIONS_BY_SET)
    ids_index: Dict[str, Any] = field(default_factory=dict)


_CACHE: Optional[_Cached] = None
_HIGHLIGHT_SET_ID: Optional[str] = None
_HIGHLIGHT_PARAMS: Optional[Dict[str, Any]] = None


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
    # Load ids index (optional; compute from sets on failure)
    try:
        with open(str(default_ids_path()), "r", encoding="utf-8") as f:
            ids_index = json.load(f) or {}
        # Minimal shape guard
        if not isinstance(ids_index, dict) or "SET_IDS" not in ids_index:
            raise ValueError("invalid ids_index shape")
        # Consistency check against current sets.json to avoid stale ids index
        try:
            built_index = _build_ids_index(sets)
            if not _ids_index_consistent(ids_index, built_index):
                logger.debug("[FSMBridge] ids_index is stale or mismatched; rebuilding from sets.json")
                ids_index = built_index
                # Best-effort: write back to disk to keep tools in sync
                try:
                    with open(str(default_ids_path()), "w", encoding="utf-8") as fw:
                        json.dump(ids_index, fw, ensure_ascii=False, indent=2, sort_keys=True)
                except Exception as exw:
                    logger.debug("[FSMBridge] failed to write rebuilt ids_index: %s", exw)
        except Exception as exb:
            logger.debug("[FSMBridge] failed ids_index consistency check: %s", exb)
    except Exception as ex:
        logger.debug("[FSMBridge] ids_index not found/invalid, rebuilding from sets: %s", ex)
        ids_index = _build_ids_index(sets)
    # Basic validation: initial state exists; state classes resolvable
    try:
        _validate_sets(sets)
    except Exception as ex:
        logger.warning("[FSMBridge] validation warnings: %s", ex)
    by_id = {s["id"]: s for s in sets.get("sets", [])}
    _CACHE = _Cached(sets=sets, assignments=assignments, by_id=by_id, anim_map=anim_map, ids_index=ids_index)
    logger.debug("[FSMBridge] cache loaded: %d sets; ids_index sets=%d", len(by_id), len(ids_index.get("SET_IDS", [])))
    return _CACHE


def reload() -> int:
    """Reload sets/assignments from disk and bump version."""
    global _CACHE
    _CACHE = None
    return publish_reload()


def set_editor_highlight_set(set_id: Optional[str]) -> None:
    """Optional: store a set_id for the Editor UI to highlight in the Sets panel.
    Pass None to clear highlight.
    """
    global _HIGHLIGHT_SET_ID, _HIGHLIGHT_PARAMS
    _HIGHLIGHT_SET_ID = set_id
    # Back-compat: if only set is provided, clear params
    _HIGHLIGHT_PARAMS = None


def get_editor_highlight_set() -> Optional[str]:
    """Return the currently highlighted set id, if any."""
    return _HIGHLIGHT_SET_ID


def set_editor_highlight_context(set_id: Optional[str], params: Optional[Dict[str, Any]]) -> None:
    """Set highlight context (set_id + params) for Editor panels to reflect hover and lint.
    Pass None to clear.
    """
    global _HIGHLIGHT_SET_ID, _HIGHLIGHT_PARAMS
    _HIGHLIGHT_SET_ID = set_id
    _HIGHLIGHT_PARAMS = dict(params) if isinstance(params, dict) else None


def get_editor_highlight_context() -> Tuple[Optional[str], Optional[Dict[str, Any]]]:
    """Return (set_id, params) of the current highlight context, if any."""
    return _HIGHLIGHT_SET_ID, (_HIGHLIGHT_PARAMS if isinstance(_HIGHLIGHT_PARAMS, dict) else None)


def lint_set_params(set_id: str, params: Optional[Dict[str, Any]]) -> list:
    """Lightweight linter for Spawner_* set params. Returns a list of warning strings.
    Rules are intentionally simple and non-fatal.
    """
    warnings: list = []
    if not set_id:
        return ["empty set_id"]
    p = params or {}
    sid = str(set_id)
    try:
        def _is_int(v):
            return isinstance(v, int) and not isinstance(v, bool)
        def _gte0(v):
            return _is_int(v) and v >= 0
        # Common checks
        if 'max_active' in p and not _gte0(p['max_active']):
            warnings.append("max_active must be integer >= 0")
        if 'restart_cooldown_frames' in p and not _gte0(p['restart_cooldown_frames']):
            warnings.append("restart_cooldown_frames must be integer >= 0")
        if 'spawn_radius' in p:
            sr = p['spawn_radius']
            if isinstance(sr, (int, float)) and sr < 0:
                warnings.append("spawn_radius must be >= 0")
            elif isinstance(sr, str) and sr.lower() not in ("random", "aleatorio", "aleatoreo"):
                warnings.append("spawn_radius string must be 'random'/'aleatorio'/'aleatoreo'")
        # Per-set expectations
        if sid == 'Spawner_Periodic_Cooldown':
            if 'cooldown_frames' not in p:
                warnings.append("cooldown_frames missing for Periodic_Cooldown")
            elif not _gte0(p['cooldown_frames']):
                warnings.append("cooldown_frames must be integer >= 0")
        elif sid == 'Spawner_Periodic_BetweenWaves':
            if 'between_waves_cooldown_frames' not in p:
                warnings.append("between_waves_cooldown_frames missing for Periodic_BetweenWaves")
            elif not _gte0(p['between_waves_cooldown_frames']):
                warnings.append("between_waves_cooldown_frames must be integer >= 0")
        elif sid == 'Spawner_Waves_Clear':
            # advance_on should be 'clear' when compiled correctly
            adv = p.get('advance_on')
            if adv and str(adv) != 'clear':
                warnings.append("advance_on should be 'clear' for Waves_Clear")
        # Shape validation
        if 'spawner_shape' in p:
            if str(p['spawner_shape']).lower() not in ('circle', 'square'):
                warnings.append("spawner_shape must be 'circle' or 'square'")
    except Exception as ex:
        warnings.append(f"linter error: {ex}")
    return warnings


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
    # Lazy import to avoid circular import during module initialization
    from .fsm_registry import get_state_class
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
    # Also expose reverse mapping: class-name -> state-id
    try:
        class_to_id = {cls_name: sid for sid, cls_name in id_to_class.items() if cls_name}
    except Exception:
        class_to_id = {}
    fsm.context["class_to_id"] = class_to_id
    # Expose transitions list for JSON-driven evaluation
    try:
        fsm.context["transitions"] = list(set_def.get("transitions", []) or [])
    except Exception:
        fsm.context["transitions"] = []
    # Always attach set_id for diagnostics and policies
    fsm.context["set_id"] = set_id
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


def _build_ids_index(sets_doc: Dict[str, Any]) -> Dict[str, Any]:
    """Build ids index structure from sets.json content.
    Output keys: SET_IDS, STATES_BY_SET, TRANSITIONS_BY_SET.
    """
    sets_list: list[str] = []
    states_by_set: Dict[str, list[str]] = {}
    trans_by_set: Dict[str, list[str]] = {}
    try:
        for s in (sets_doc or {}).get("sets", []) or []:
            if not isinstance(s, dict):
                continue
            sid = s.get("id")
            if not isinstance(sid, str):
                continue
            sets_list.append(sid)
            states = s.get("states") or []
            transitions = s.get("transitions") or []
            states_by_set[sid] = [st.get("id") for st in states if isinstance(st, dict) and isinstance(st.get("id"), str)]
            trans_by_set[sid] = [tr.get("id") for tr in transitions if isinstance(tr, dict) and isinstance(tr.get("id"), str)]
    except Exception:
        # Best-effort; keep partial data
        pass
    return {
        "SET_IDS": sets_list,
        "STATES_BY_SET": states_by_set,
        "TRANSITIONS_BY_SET": trans_by_set,
    }


def _ids_index_consistent(a: Dict[str, Any], b: Dict[str, Any]) -> bool:
    """Return True if two ids index structures are equivalent for our purposes.
    We compare the set ids and per-set keys content-wise (order-insensitive for safety).
    """
    try:
        aset = set(a.get("SET_IDS", []) or [])
        bset = set(b.get("SET_IDS", []) or [])
        if aset != bset:
            return False
        astates = a.get("STATES_BY_SET", {}) or {}
        bstates = b.get("STATES_BY_SET", {}) or {}
        atrans = a.get("TRANSITIONS_BY_SET", {}) or {}
        btrans = b.get("TRANSITIONS_BY_SET", {}) or {}
        # Ensure same keys for states/transitions
        if set(astates.keys()) != set(bstates.keys()):
            return False
        if set(atrans.keys()) != set(btrans.keys()):
            return False
        # Optionally compare contents (order-insensitive)
        for k in astates.keys():
            if set(astates.get(k) or []) != set(bstates.get(k) or []):
                return False
        for k in atrans.keys():
            if set(atrans.get(k) or []) != set(btrans.get(k) or []):
                return False
        return True
    except Exception:
        return False


def get_ids_index() -> Dict[str, Any]:
    """Return the cached ids index JSON for tooling/runtime.
    Structure: {"SET_IDS": [...], "STATES_BY_SET": {...}, "TRANSITIONS_BY_SET": {...}}
    """
    return _ensure_cache().ids_index


def get_set_ids() -> list:
    """Return list of set ids (ordered as in JSON export)."""
    try:
        return list(_ensure_cache().ids_index.get("SET_IDS", []) or [])
    except Exception:
        return []


def get_state_ids(set_id: str) -> list:
    """Return list of state ids for a set id."""
    try:
        return list((_ensure_cache().ids_index.get("STATES_BY_SET", {}) or {}).get(set_id, []) or [])
    except Exception:
        return []


def get_transition_ids(set_id: str) -> list:
    """Return list of transition ids for a set id."""
    try:
        return list((_ensure_cache().ids_index.get("TRANSITIONS_BY_SET", {}) or {}).get(set_id, []) or [])
    except Exception:
        return []


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
