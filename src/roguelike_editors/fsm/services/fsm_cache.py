"""Caching and loading utilities for FSM sets, assignments, and metadata."""
from __future__ import annotations
from typing import Any, Dict, Optional
from dataclasses import dataclass, field
import json
import logging

from roguelike_editors.fsm.services.fsm_persistence.fsm_persistence import (
    load_all,
    default_animation_map_path,
    default_ids_path,
    load_animation_map,
)

from .fsm_ids import build_ids_index, ids_index_consistent

logger = logging.getLogger(__name__)


@dataclass
class Cached:
    sets: Dict[str, Any]
    assignments: Dict[str, Any]
    by_id: Dict[str, Dict[str, Any]]
    anim_map: Dict[str, Any]
    ids_index: Dict[str, Any] = field(default_factory=dict)


_CACHE: Optional[Cached] = None


def ensure_cache() -> Cached:
    """Ensure cached data is loaded and return it."""
    global _CACHE
    if _CACHE is not None:
        return _CACHE

    # Back-compat: allow tests (and bridge) to inject a prebuilt cache via fsm_runtime_bridge._CACHE
    # Import locally to avoid circular import at module initialization time.
    try:
        from . import fsm_runtime_bridge as fbr  # type: ignore
        ext_cache = getattr(fbr, "_CACHE", None)
        if ext_cache is not None:
            _CACHE = ext_cache  # type: ignore[assignment]
            return _CACHE  # type: ignore[return-value]
    except Exception:
        pass

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
        if not isinstance(ids_index, dict) or "SET_IDS" not in ids_index:
            raise ValueError("invalid ids_index shape")
        # Consistency check against current sets.json to avoid stale ids index
        try:
            built_index = build_ids_index(sets)
            if not ids_index_consistent(ids_index, built_index):
                logger.debug("[FSMBridge] ids_index is stale or mismatched; rebuilding from sets.json")
                ids_index = built_index
                try:
                    with open(str(default_ids_path()), "w", encoding="utf-8") as fw:
                        json.dump(ids_index, fw, ensure_ascii=False, indent=2, sort_keys=True)
                except Exception as exw:
                    logger.debug("[FSMBridge] failed to write rebuilt ids_index: %s", exw)
        except Exception as exb:
            logger.debug("[FSMBridge] failed ids_index consistency check: %s", exb)
    except Exception as ex:
        logger.debug("[FSMBridge] ids_index not found/invalid, rebuilding from sets: %s", ex)
        ids_index = build_ids_index(sets)

    # Basic validation: initial state exists; state classes resolvable
    try:
        _validate_sets(sets)
    except Exception as ex:
        logger.warning("[FSMBridge] validation warnings: %s", ex)

    by_id = {s["id"]: s for s in sets.get("sets", [])}
    _CACHE = Cached(sets=sets, assignments=assignments, by_id=by_id, anim_map=anim_map, ids_index=ids_index)
    logger.debug("[FSMBridge] cache loaded: %d sets; ids_index sets=%d", len(by_id), len(ids_index.get("SET_IDS", [])))
    return _CACHE


def clear_cache() -> None:
    """Clear in-memory cache to force reload on next access."""
    global _CACHE
    _CACHE = None


# Convenience accessors -----------------------------------------------------

def get_snapshot() -> Dict[str, Any]:
    """Return editor snapshot (raw sets JSON)."""
    return ensure_cache().sets


def get_set(set_id: str) -> Optional[Dict[str, Any]]:
    return ensure_cache().by_id.get(set_id)


# Internal validation -------------------------------------------------------

def _validate_sets(sets_doc: Dict[str, Any]) -> None:
    """Log warnings for common issues in sets.json."""
    # Lazy import to avoid circular
    from .fsm_registry import get_state_class  # type: ignore

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
    for p in problems:
        logger.warning("[FSMBridge][validate] %s", p)
