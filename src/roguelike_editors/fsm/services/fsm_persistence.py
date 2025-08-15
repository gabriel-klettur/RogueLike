"""Persistence layer for FSM Sets and Assignments.

- load_sets / save_sets to data/fsm/sets.json
- load_assignments / save_assignments to data/fsm/assignments.json
- validate against data/fsm/schema.json (optional)
"""
from __future__ import annotations
from typing import Any, Dict, Tuple, List
from pathlib import Path
import logging

logger = logging.getLogger(__name__)

# Feature toggle: auto-include special states in all sets during normalization
# Currently includes only the Damage state.
AUTO_INCLUDE_DAMAGE: bool = True


def _project_root() -> Path:
    """Resolve the project root by locating the 'src' directory and returning its parent."""
    here = Path(__file__).resolve()
    for p in here.parents:
        if p.name == 'src':
            return p.parent
    # Fallback: assume standard depth .../RogueLike/src/roguelike_editors/fsm/services
    try:
        return here.parents[4]
    except Exception:
        return here.parent


def default_sets_path() -> Path:
    return _project_root() / "data" / "fsm" / "sets.json"


def default_schema_path() -> Path:
    return _project_root() / "data" / "fsm" / "schema.json"


def default_assignments_path() -> Path:
    return _project_root() / "data" / "fsm" / "assignments.json"


def default_layouts_path() -> Path:
    """Path for FSM editor graph layouts (node positions per set)."""
    return _project_root() / "data" / "fsm" / "layouts.json"


def default_animation_map_path() -> Path:
    """Path for FSM animation map (state-class -> animation base, with per-set overrides)."""
    return _project_root() / "data" / "fsm" / "animation_map.json"


def load_sets(path: str | Path) -> Dict[str, Any]:
    """Load FSM sets from JSON file. TODO: implement fully."""
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


_LAST_LINT: Tuple[List[str], List[str]] = ([], [])


def get_last_lint() -> Tuple[List[str], List[str]]:
    """Return the last (warnings, errors) produced by save_sets or _lint_sets.
    Useful for editor UI to surface results after a save.
    """
    return _LAST_LINT


def save_sets(data: Dict[str, Any], path: str | Path) -> Tuple[List[str], List[str]]:
    """Save FSM sets to JSON file (pretty, deterministic).
    Professional flow: (1) normalize/minimally migrate, (2) validate (if schema present),
    (3) save pretty JSON, (4) generate constants for code references.
    """
    import json
    # 1) Normalize/migrate in-memory (ids/defaults)
    try:
        _ensure_ids_and_defaults(data)
    except Exception:
        # Keep going even if normalization fails
        pass
    # 2) Validate (optional if schema missing)
    try:
        validate(data, default_schema_path())
    except Exception:
        # Do not block save during authoring; validation errors should be surfaced by caller/UI
        pass
    # 2b) Lint cross-field rules (non-blocking; surface via logger, allow caller to decide UI)
    warns: List[str] = []
    errs: List[str] = []
    try:
        warns, errs = _lint_sets(data)
        # Cache last lint for UI access
        global _LAST_LINT
        _LAST_LINT = (list(warns), list(errs))
        for msg in warns:
            try:
                logger.warning("[FSMSets][lint][warning] %s", msg)
            except Exception:
                pass
        if errs:
            # Raise to allow interested callers to catch and surface; we still swallow below
            raise ValueError("; ".join(errs))
    except Exception:
        # Do not block authoring; callers may surface errors
        pass
    # 3) Save pretty, deterministic
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(str(p), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)
    # 4) Codegen of constants for runtime-safe references
    try:
        _generate_code_ids(data)
    except Exception:
        # Non-fatal if codegen fails
        pass
    # Return lint results so callers can surface UI feedback
    try:
        return _LAST_LINT
    except Exception:
        return (warns, errs)


def load_assignments(path: str | Path) -> Dict[str, Any]:
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def save_assignments(data: Dict[str, Any], path: str | Path) -> None:
    import json
    with open(str(path), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)


def load_animation_map(path: str | Path) -> Dict[str, Any]:
    """Load animation_map.json. Returns an object with keys 'default' and optional 'overrides'."""
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)

def save_animation_map(data: Dict[str, Any], path: str | Path) -> None:
    """Save animation_map.json in pretty, deterministic format."""
    import json
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(str(p), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)


def load_layouts(path: str | Path) -> Dict[str, Any]:
    """Load FSM editor graph layouts.
    Structure: {"by_set": {set_id: {"nodes": {node_id: {"x": int, "y": int}}}}}
    """
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


def save_layouts(data: Dict[str, Any], path: str | Path) -> None:
    """Save FSM editor graph layouts (pretty)."""
    import json
    # Ensure parent exists
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(str(p), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)


def validate(data: Dict[str, Any], schema_path: str | Path) -> None:
    """Validate data with JSON Schema if 'jsonschema' is available.
    Raise ValueError on validation errors. No-op if schema not found.
    """
    try:
        import json
        import jsonschema  # type: ignore
        with open(str(schema_path), "r", encoding="utf-8") as f:
            schema = json.load(f)
        jsonschema.validate(instance=data, schema=schema)
    except FileNotFoundError:
        # Schema optional during early development
        return
    except ImportError:
        # Validation optional if dependency not present
        return


def load_all(
    sets_path: Path | None = None,
    assignments_path: Path | None = None,
    schema_path: Path | None = None,
) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    """Load sets and assignments with optional validation. Returns (sets, assignments)."""
    sets_path = sets_path or default_sets_path()
    assignments_path = assignments_path or default_assignments_path()
    schema_path = schema_path or default_schema_path()

    sets = load_sets(sets_path)
    try:
        validate(sets, schema_path)
    except Exception:
        # Keep running even if schema invalid or not present
        pass
    try:
        assignments = load_assignments(assignments_path)
    except FileNotFoundError:
        assignments = {"by_archetype": {}, "by_eid": {}}
    return sets, assignments


# --- Helpers: normalization (ids/defaults) and code generation -----------------

def _ensure_ids_and_defaults(data: Dict[str, Any]) -> None:
    """Ensure each node/state and transition has stable ids and minimal defaults.
    - states[].id must be string; ensure props present
    - transitions[].id assigned if missing; ensure from/to/when are strings
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
                # Optional layout fields (kept if present); don't force here
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


def _lint_sets(data: Dict[str, Any]) -> Tuple[List[str], List[str]]:
    """Static linting for cross-field rules not covered by JSON Schema.
    Returns (warnings, errors). Does not mutate data.
    Checks per set:
    - unique state ids and transition ids
    - transitions.from/to must reference existing state ids
    - initial must be present and reference an existing state id
    - duplicate transitions (same from,to,when) -> warning
    - conflicting node-level initial flags vs set.initial -> warning
    - unreachable states from initial -> warning
    """
    warns: List[str] = []
    errs: List[str] = []
    if not isinstance(data, dict):
        return warns, ["Document is not an object"]
    sets = data.get("sets")
    if not isinstance(sets, list):
        return warns, ["'sets' must be an array"]
    seen_set_ids: set[str] = set()
    for s in sets:
        sid = s.get("id")
        if not isinstance(sid, str) or not sid:
            errs.append("set: missing/invalid id")
            sid = "<unknown>"
        elif sid in seen_set_ids:
            errs.append(f"set '{sid}': duplicate set id")
        else:
            seen_set_ids.add(sid)
        # States
        states = s.get("states") or []
        if not isinstance(states, list):
            errs.append(f"set '{sid}': states must be an array")
            states = []
        state_ids: list[str] = []
        seen_state_ids: set[str] = set()
        for st in states:
            nid = st.get("id")
            if not isinstance(nid, str) or not nid:
                errs.append(f"set '{sid}': state with missing/invalid id")
                continue
            if nid in seen_state_ids:
                errs.append(f"set '{sid}': duplicate state id '{nid}'")
            else:
                seen_state_ids.add(nid)
                state_ids.append(nid)
        # Initial
        initial = s.get("initial")
        if not isinstance(initial, str) or not initial:
            errs.append(f"set '{sid}': missing/invalid initial state id")
        elif initial not in seen_state_ids:
            errs.append(f"set '{sid}': initial '{initial}' not found among states")
        # Transitions
        transitions = s.get("transitions") or []
        if not isinstance(transitions, list):
            errs.append(f"set '{sid}': transitions must be an array")
            transitions = []
        seen_tr_ids: set[str] = set()
        seen_sig: set[tuple[str, str, str]] = set()
        for tr in transitions:
            tid = tr.get("id")
            if isinstance(tid, str) and tid:
                if tid in seen_tr_ids:
                    errs.append(f"set '{sid}': duplicate transition id '{tid}'")
                else:
                    seen_tr_ids.add(tid)
            else:
                warns.append(f"set '{sid}': transition without id (will be auto-assigned)")
            fr = tr.get("from"); to = tr.get("to"); wh = tr.get("when")
            if not isinstance(fr, str) or fr not in seen_state_ids:
                errs.append(f"set '{sid}': transition '{tid or '<no-id>'}' from invalid/missing state '{fr}'")
            if not isinstance(to, str) or to not in seen_state_ids:
                errs.append(f"set '{sid}': transition '{tid or '<no-id>'}' to invalid/missing state '{to}'")
            if not isinstance(wh, str) or not wh:
                errs.append(f"set '{sid}': transition '{tid or '<no-id>'}' missing/invalid 'when'")
            sig = (str(fr), str(to), str(wh))
            if all(isinstance(x, str) and x for x in sig):
                if sig in seen_sig:
                    warns.append(f"set '{sid}': duplicate transition triple {sig}")
                else:
                    seen_sig.add(sig)
        # Node-level initial flags consistency (optional)
        flagged = [st.get("id") for st in states if isinstance(st, dict) and st.get("initial") is True and isinstance(st.get("id"), str)]
        if len(flagged) > 1:
            warns.append(f"set '{sid}': multiple states flagged initial (using set.initial='{initial}')")
        elif len(flagged) == 1 and isinstance(initial, str) and flagged[0] != initial:
            warns.append(f"set '{sid}': state '{flagged[0]}' flagged initial but set.initial is '{initial}'")
        # Unreachable states from initial (warning only)
        if isinstance(initial, str) and initial in seen_state_ids:
            adj: dict[str, list[str]] = {nid: [] for nid in state_ids}
            for tr in transitions:
                fr = tr.get("from"); to = tr.get("to")
                if isinstance(fr, str) and isinstance(to, str) and fr in adj and to in adj:
                    adj[fr].append(to)
            reachable: set[str] = set()
            stack = [initial]
            while stack:
                cur = stack.pop()
                if cur in reachable:
                    continue
                reachable.add(cur)
                for nxt in adj.get(cur, []):
                    if nxt not in reachable:
                        stack.append(nxt)
            unreachable = [nid for nid in state_ids if nid not in reachable]
            # Identify externally-entered/special states to suppress unreachable warnings
            external_ids: set[str] = set()
            try:
                for st in states:
                    nid = st.get("id")
                    if not isinstance(nid, str):
                        continue
                    cls = st.get("class")
                    # Explicit flag
                    if st.get("external_entry") is True:
                        external_ids.add(nid)
                        continue
                    # Semantics via 'special'
                    spec = st.get("special")
                    if isinstance(spec, str) and spec.lower() in ("damage", "external", "interrupt", "alert"):
                        external_ids.add(nid)
                        continue
                    # Known built-ins
                    if (nid in ("Damage", "AlertChase")) or (cls in ("DamageState", "AlertChaseState")):
                        external_ids.add(nid)
            except Exception:
                # Best-effort; never block linting
                external_ids = external_ids
            filtered_unreachable = [nid for nid in unreachable if nid not in external_ids]
            if filtered_unreachable:
                warns.append(f"set '{sid}': unreachable states from initial: {filtered_unreachable}")
    return warns, errs


def _generate_code_ids(data: Dict[str, Any]) -> None:
    """Generate a Python module with constants for FSM sets, states, and transitions.
    Output: src/roguelike_game/fsm/fsm_ids.py
    Structure:
      SET_IDS = ["SetId", ...]
      STATES_BY_SET = {"SetId": ["StateId", ...], ...}
      TRANSITIONS_BY_SET = {"SetId": ["TransitionId", ...], ...}
    """
    root = _project_root()
    out_dir = root / "src" / "roguelike_game" / "fsm"
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / "fsm_ids.py"

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

    # Emit module
    header = """# Generated by fsm_persistence._generate_code_ids — DO NOT EDIT BY HAND\n\n"""
    body = [
        "SET_IDS = " + repr(sets_list),
        "STATES_BY_SET = " + repr(states_by_set),
        "TRANSITIONS_BY_SET = " + repr(trans_by_set),
        "\n__all__ = ['SET_IDS', 'STATES_BY_SET', 'TRANSITIONS_BY_SET']\n",
    ]
    with open(str(out_path), "w", encoding="utf-8") as f:
        f.write(header)
        f.write("\n".join(body))


__all__ = [
    "default_sets_path",
    "default_assignments_path",
    "default_layouts_path",
    "default_schema_path",
    "default_animation_map_path",
    "load_sets",
    "save_sets",
    "get_last_lint",
    "load_assignments",
    "save_assignments",
    "load_animation_map",
    "save_animation_map",
    "load_layouts",
    "save_layouts",
    "validate",
    "load_all",
]
