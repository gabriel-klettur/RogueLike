from __future__ import annotations
from typing import Any, Dict, List

# Feature toggle: auto-include special states in all sets during normalization
# Currently includes only the Damage state.
AUTO_INCLUDE_DAMAGE: bool = True


def _ensure_ids_and_defaults(data: Dict[str, Any]) -> None:
    """Ensure each node/state and transition has stable ids and minimal defaults.
    - states[].id must be string; ensure props present
    - transitions[].id assigned if missing; ensure from/to/when are strings
    - migrate 'when' <-> 'event' (duplicate for compatibility)
    - add defaults: priority=0, cooldown_frames=0, actions=[]
    - mark global transitions if from=="*"
    This function mutates the passed 'data'.
    """
    import uuid

    if not isinstance(data, dict):
        return
    # Ensure version minimum
    try:
        ver = int(data.get("version", 0))
    except Exception:
        ver = 0
    if ver < 1:
        data["version"] = 1
    sets: List[Dict[str, Any]] = data.get("sets") or []
    if not isinstance(sets, list):
        return
    for s in sets:
        # States (nodes)
        states = s.get("states") or []
        if isinstance(states, list):
            for st in states:
                if not isinstance(st.get("id"), str):
                    st["id"] = f"node_{uuid.uuid4().hex[:8]}"
                # Defaults
                if "props" not in st or not isinstance(st.get("props"), dict):
                    st["props"] = {}
            # Optional: ensure Damage state exists across all sets (external-entry)
            if AUTO_INCLUDE_DAMAGE:
                try:
                    has_damage = False
                    for st in states:
                        sid = st.get("id")
                        sclass = st.get("class")
                        if sid == "Damage" or sclass == "DamageState":
                            has_damage = True
                            break
                    if not has_damage:
                        states.append({
                            "id": "Damage",
                            "label": "Damage",
                            "class": "DamageState",
                            "special": "damage",
                            "external_entry": True,
                        })
                        s["states"] = states
                except Exception:
                    # Don't block normalization if inclusion fails
                    pass
        # Transitions (edges)
        trans = s.get("transitions") or []
        if isinstance(trans, list):
            for tr in trans:
                if not isinstance(tr.get("id"), str):
                    tr["id"] = f"tr_{uuid.uuid4().hex[:8]}"
                # Coerce required fields to str if present
                for k in ("from", "to", "when"):
                    v = tr.get(k)
                    if v is not None and not isinstance(v, str):
                        tr[k] = str(v)
                # Migrate/duplicate when<->event for compatibility
                try:
                    ev = tr.get("event")
                    wh = tr.get("when")
                    if isinstance(wh, str) and (not isinstance(ev, str) or not ev):
                        tr["event"] = wh
                    elif isinstance(ev, str) and (not isinstance(wh, str) or not wh):
                        tr["when"] = ev
                except Exception:
                    pass
                # Defaults for new fields
                if "priority" not in tr or not isinstance(tr.get("priority"), int):
                    try:
                        tr["priority"] = int(tr.get("priority", 0))
                    except Exception:
                        tr["priority"] = 0
                if "cooldown_frames" not in tr or not isinstance(tr.get("cooldown_frames"), int):
                    try:
                        tr["cooldown_frames"] = int(tr.get("cooldown_frames", 0))
                    except Exception:
                        tr["cooldown_frames"] = 0
                # Actions default to list; keep strings as-is for compatibility with schema anyOf
                if "actions" not in tr or not isinstance(tr.get("actions"), list):
                    tr["actions"] = []
                # Normalize global transitions
                try:
                    if (tr.get("from") == "*") and tr.get("global") is not True:
                        tr["global"] = True
                except Exception:
                    pass
