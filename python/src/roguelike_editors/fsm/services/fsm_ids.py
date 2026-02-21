"""IDs index helpers for FSM sets.

- Build index of set IDs, state IDs per set, and transition IDs per set.
- Compare two indices for logical equivalence (order-insensitive).
"""
from __future__ import annotations
from typing import Any, Dict, List


def build_ids_index(sets_doc: Dict[str, Any]) -> Dict[str, Any]:
    """Build ids index structure from sets.json-like content.

    Output keys:
    - SET_IDS: List[str]
    - STATES_BY_SET: Dict[set_id, List[state_id]]
    - TRANSITIONS_BY_SET: Dict[set_id, List[transition_id]]
    """
    sets_list: List[str] = []
    states_by_set: Dict[str, List[str]] = {}
    trans_by_set: Dict[str, List[str]] = {}
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
            states_by_set[sid] = [
                st.get("id") for st in states if isinstance(st, dict) and isinstance(st.get("id"), str)
            ]
            trans_by_set[sid] = [
                tr.get("id") for tr in transitions if isinstance(tr, dict) and isinstance(tr.get("id"), str)
            ]
    except Exception:
        # Best-effort; keep partial data
        pass
    return {
        "SET_IDS": sets_list,
        "STATES_BY_SET": states_by_set,
        "TRANSITIONS_BY_SET": trans_by_set,
    }


def ids_index_consistent(a: Dict[str, Any], b: Dict[str, Any]) -> bool:
    """Return True if two ids index structures are equivalent for our purposes.

    We compare set ids and per-set keys content-wise, order-insensitive.
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
        if set(astates.keys()) != set(bstates.keys()):
            return False
        if set(atrans.keys()) != set(btrans.keys()):
            return False
        for k in astates.keys():
            if set(astates.get(k) or []) != set(bstates.get(k) or []):
                return False
        for k in atrans.keys():
            if set(atrans.get(k) or []) != set(btrans.get(k) or []):
                return False
        return True
    except Exception:
        return False
