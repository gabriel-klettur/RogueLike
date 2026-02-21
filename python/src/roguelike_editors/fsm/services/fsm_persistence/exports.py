from __future__ import annotations
from typing import Any, Dict, List
from pathlib import Path
import json

from .paths import default_ids_path


def _export_ids_json(data: Dict[str, Any], out_path: str | Path | None = None) -> None:
    """Export an ids index JSON under data/fsm/ with keys SET_IDS, STATES_BY_SET, TRANSITIONS_BY_SET."""
    out = Path(out_path) if out_path is not None else default_ids_path()

    sets_list: List[str] = []
    states_by_set: Dict[str, List[str]] = {}
    trans_by_set: Dict[str, List[str]] = {}

    sets: List[Dict[str, Any]] = (data or {}).get("sets") or []
    if isinstance(sets, list):
        for s in sets:
            sid = s.get("id")
            if not isinstance(sid, str):
                continue
            sets_list.append(sid)
            # States
            states = s.get("states") or []
            if isinstance(states, list):
                states_by_set[sid] = [st.get("id") for st in states if isinstance(st.get("id"), str)]
            else:
                states_by_set[sid] = []
            # Transitions
            trans = s.get("transitions") or []
            if isinstance(trans, list):
                trans_by_set[sid] = [tr.get("id") for tr in trans if isinstance(tr.get("id"), str)]
            else:
                trans_by_set[sid] = []

    payload = {
        "SET_IDS": sets_list,
        "STATES_BY_SET": states_by_set,
        "TRANSITIONS_BY_SET": trans_by_set,
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(str(out), "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2, sort_keys=True)


def _generate_code_ids(data: Dict[str, Any]) -> None:
    """Deprecated: kept for backward compatibility. Now delegates to JSON export.
    Previously wrote src/roguelike_game/fsm/fsm_ids.py; now writes data/fsm/fsm_ids.json.
    """
    try:
        _export_ids_json(data, default_ids_path())
    except Exception:
        # Best-effort; do not raise in authoring flows
        pass
