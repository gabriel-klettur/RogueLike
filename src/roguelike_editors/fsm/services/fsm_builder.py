"""FSM builder utilities that compile a set definition into a FiniteStateMachine."""
from __future__ import annotations
from typing import Any, Dict, Tuple
import logging

from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine

from .fsm_cache import ensure_cache

logger = logging.getLogger(__name__)


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
    from .fsm_registry import get_state_class  # type: ignore

    cls = get_state_class(class_name) if class_name else None
    if cls is None:
        raise ValueError(f"FSM state class not found: {class_name}")

    fsm = FiniteStateMachine(cls())

    # Resolve and attach animation map for this set (default + per-set override)
    set_id = set_def.get("id", "") or ""
    try:
        c = ensure_cache()
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

    # Reverse mapping: class-name -> state-id
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
        fsm.context.setdefault("allow_death", True)
        fsm.context.setdefault("allow_damage", True)
        fsm.context.setdefault("allow_unconscious", True)

        # If no explicit Damage transition exists, fall back to a sensible Monster policy.
        if "damage_next_class" not in fsm.context:
            if "AlertChaseState" in allowed_classes:
                fsm.context["damage_next_class"] = "AlertChaseState"
            elif "ChaseState" in allowed_classes:
                fsm.context["damage_next_class"] = "ChaseState"
            else:
                fsm.context["damage_next_class"] = "PatrolState"

    return fsm, initial  # type: ignore[return-value]
