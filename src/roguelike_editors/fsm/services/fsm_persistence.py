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


def default_ids_path() -> Path:
    """Path for exported FSM ids index JSON.
    Structure mirrors previous Python constants as JSON keys:
      {"SET_IDS": [...], "STATES_BY_SET": {...}, "TRANSITIONS_BY_SET": {...}}
    """
    return _project_root() / "data" / "fsm" / "fsm_ids.json"

def load_sets(path: str | Path) -> Dict[str, Any]:
    """Load FSM sets from JSON file. TODO: implement fully."""
    import json
    with open(str(path), "r", encoding="utf-8") as f:
        return json.load(f)


_LAST_LINT: Tuple[List[str], List[str]] = ([], [])
# Enriched cached lint items for editor (list of dicts with metadata)
_LAST_LINT_ENRICHED: List[Dict[str, Any]] = []


def get_last_lint() -> Tuple[List[str], List[str]]:
    """Return the last (warnings, errors) produced by save_sets or _lint_sets.
    Useful for editor UI to surface results after a save.
    """
    return _LAST_LINT


def get_last_lint_enriched() -> List[Dict[str, Any]]:
    """Return the last enriched lint items produced by save_sets or _lint_sets.
    Each item is a dict: {severity, scope, set_id, state_id?, transition_id?, from?, to?, event?, message}
    """
    return list(_LAST_LINT_ENRICHED)


def save_sets(data: Dict[str, Any], path: str | Path) -> Tuple[List[str], List[str]]:
    """Save FSM sets to JSON file (pretty, deterministic).
    Professional flow: (1) normalize/minimally migrate, (2) validate (if schema present),
    (3) save pretty JSON, (4) export ids index JSON for tooling/runtime.
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
    enriched: List[Dict[str, Any]] = []
    try:
        warns, errs, enriched = _lint_sets_both(data)
        # Cache last lint for UI access
        global _LAST_LINT, _LAST_LINT_ENRICHED
        _LAST_LINT = (list(warns), list(errs))
        _LAST_LINT_ENRICHED = list(enriched)
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
    # 4) Export ids index JSON (non-fatal on failure)
    try:
        _export_ids_json(data, default_ids_path())
    except Exception:
        # Non-fatal if export fails
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
    # Compute and cache initial lint for editor badges so they appear on first render
    try:
        warns, errs, enriched = _lint_sets_both(sets)
        global _LAST_LINT, _LAST_LINT_ENRICHED
        _LAST_LINT = (list(warns), list(errs))
        _LAST_LINT_ENRICHED = list(enriched)
    except Exception:
        # Don't block load flow if linting fails
        pass
    return sets, assignments


# --- Helpers: normalization (ids/defaults) and code generation -----------------

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


def _lint_sets_both(data: Dict[str, Any]) -> Tuple[List[str], List[str], List[Dict[str, Any]]]:
    """Static linting producing both flat lists and enriched items.
    Returns (warnings, errors, enriched_items).
    Each enriched item: {severity, scope, set_id, state_id?, transition_id?, from?, to?, event?, message}
    Does not mutate data.
    """
    warns: List[str] = []
    errs: List[str] = []
    enriched: List[Dict[str, Any]] = []

    def add(sev: str, msg: str, *, set_id: str | None = None, state_id: str | None = None,
            transition: Dict[str, Any] | None = None) -> None:
        if sev == 'warning':
            warns.append(msg)
        else:
            errs.append(msg)
        item: Dict[str, Any] = {
            'severity': sev,
            'scope': 'transition' if transition is not None else ('state' if state_id else 'set'),
            'set_id': set_id,
            'message': msg,
        }
        if state_id:
            item['state_id'] = state_id
        if transition is not None:
            try:
                item['transition_id'] = transition.get('id')
                item['from'] = transition.get('from')
                item['to'] = transition.get('to')
                ev = transition.get('event') if isinstance(transition.get('event'), str) and transition.get('event') else transition.get('when')
                item['event'] = ev
            except Exception:
                pass
        enriched.append(item)
    if not isinstance(data, dict):
        return warns, ["Document is not an object"], enriched
    sets = data.get("sets")
    if not isinstance(sets, list):
        return warns, ["'sets' must be an array"], enriched
    seen_set_ids: set[str] = set()
    for s in sets:
        sid = s.get("id")
        if not isinstance(sid, str) or not sid:
            add('error', "set: missing/invalid id")
            sid = "<unknown>"
        elif sid in seen_set_ids:
            add('error', f"set '{sid}': duplicate set id", set_id=sid)
        else:
            seen_set_ids.add(sid)
        # States
        states = s.get("states") or []
        if not isinstance(states, list):
            add('error', f"set '{sid}': states must be an array", set_id=sid)
            states = []
        state_ids: list[str] = []
        seen_state_ids: set[str] = set()
        # Map state id -> class to support semantic lint (e.g., after_attack)
        state_class_by_id: dict[str, str] = {}
        for st in states:
            nid = st.get("id")
            if not isinstance(nid, str) or not nid:
                add('error', f"set '{sid}': state with missing/invalid id", set_id=sid)
                continue
            if nid in seen_state_ids:
                add('error', f"set '{sid}': duplicate state id '{nid}'", set_id=sid, state_id=nid)
            else:
                seen_state_ids.add(nid)
                state_ids.append(nid)
                try:
                    scls = st.get("class")
                    if isinstance(scls, str):
                        state_class_by_id[nid] = scls
                except Exception:
                    pass
        # Initial
        initial = s.get("initial")
        if not isinstance(initial, str) or not initial:
            add('error', f"set '{sid}': missing/invalid initial state id", set_id=sid)
        elif initial not in seen_state_ids:
            add('error', f"set '{sid}': initial '{initial}' not found among states", set_id=sid)
        # Transitions
        transitions = s.get("transitions") or []
        if not isinstance(transitions, list):
            add('error', f"set '{sid}': transitions must be an array", set_id=sid)
            transitions = []
        seen_tr_ids: set[str] = set()
        seen_sig: set[tuple[str, str, str]] = set()
        has_global_transition = False
        for tr in transitions:
            tid = tr.get("id")
            if isinstance(tid, str) and tid:
                if tid in seen_tr_ids:
                    add('error', f"set '{sid}': duplicate transition id '{tid}'", set_id=sid, transition=tr)
                else:
                    seen_tr_ids.add(tid)
            else:
                add('warning', f"set '{sid}': transition without id (will be auto-assigned)", set_id=sid, transition=tr)
            fr = tr.get("from"); to = tr.get("to");
            # Allow global transitions via from=="*"
            if not (isinstance(fr, str) and (fr == "*" or fr in seen_state_ids)):
                add('error', f"set '{sid}': transition '{tid or '<no-id>'}' from invalid/missing state '{fr}' (use '*' for global)", set_id=sid, transition=tr)
            if isinstance(fr, str) and fr == "*":
                has_global_transition = True
            if not isinstance(to, str) or to not in seen_state_ids:
                add('error', f"set '{sid}': transition '{tid or '<no-id>'}' to invalid/missing state '{to}'", set_id=sid, transition=tr)
            # Accept 'event' preferred, fallback to 'when'
            ev = tr.get("event") if isinstance(tr.get("event"), str) and tr.get("event") else tr.get("when")
            if not isinstance(ev, str) or not ev:
                add('error', f"set '{sid}': transition '{tid or '<no-id>'}' missing/invalid 'event/when'", set_id=sid, transition=tr)
            sig = (str(fr), str(to), str(ev))
            if all(isinstance(x, str) and x for x in sig):
                if sig in seen_sig:
                    add('warning', f"set '{sid}': duplicate transition triple {sig}", set_id=sid, transition=tr)
                else:
                    seen_sig.add(sig)
            # priority/cooldown_frames validation (non-negative ints)
            try:
                pr = tr.get("priority")
                if pr is not None and (not isinstance(pr, int) or pr < 0):
                    add('warning', f"set '{sid}': transition '{tid or '<no-id>'}' has invalid priority='{pr}' (must be int>=0)", set_id=sid, transition=tr)
            except Exception:
                pass
            try:
                cd = tr.get("cooldown_frames")
                if cd is not None and (not isinstance(cd, int) or cd < 0):
                    add('warning', f"set '{sid}': transition '{tid or '<no-id>'}' has invalid cooldown_frames='{cd}' (must be int>=0)", set_id=sid, transition=tr)
            except Exception:
                pass
            # guard basic validation
            try:
                guard = tr.get("guard")
                if guard is not None:
                    if not isinstance(guard, dict):
                        add('warning', f"set '{sid}': transition '{tid or '<no-id>'}' guard must be an object", set_id=sid, transition=tr)
                    else:
                        op = guard.get("op")
                        allowed_ops = {"and", "or", "not", "cmp", "const", "get"}
                        if op not in allowed_ops:
                            add('warning', f"set '{sid}': transition '{tid or '<no-id>'}' guard.op='{op}' not in {sorted(allowed_ops)}", set_id=sid, transition=tr)
                        if op == "cmp":
                            cmp_op = guard.get("cmp")
                            allowed_cmp = {"==", "!=", "<", "<=", ">", ">="}
                            if cmp_op not in allowed_cmp:
                                add('warning', f"set '{sid}': transition '{tid or '<no-id>'}' guard.cmp='{cmp_op}' not in {sorted(allowed_cmp)}", set_id=sid, transition=tr)
            except Exception:
                pass
        # Semantic linting for 'after_attack' transitions
        try:
            # Detect if the set defines any Attack-like state; if so, suppress the generic hint
            has_attack_state = False
            try:
                for st in states:
                    if not isinstance(st, dict):
                        continue
                    sid2 = st.get("id") or ""
                    scls2 = st.get("class") or ""
                    if (isinstance(sid2, str) and "Attack" in sid2) or (isinstance(scls2, str) and "Attack" in scls2):
                        has_attack_state = True
                        break
            except Exception:
                has_attack_state = has_attack_state
            after_attack_hint_added = False
            for tr in transitions:
                wh = tr.get("when")
                if wh != "after_attack":
                    continue
                fr = tr.get("from")
                tid = tr.get("id")
                if not isinstance(fr, str):
                    continue
                cls = state_class_by_id.get(fr, "")
                from_is_attack = (fr == "Attack") or ("Attack" in fr)
                class_is_attack = (cls == "AttackState") or ("Attack" in cls)
                if not (from_is_attack or class_is_attack):
                    add('warning', f"set '{sid}': transition '{tid or '<no-id>'}' uses 'after_attack' but 'from' state '{fr}' is not an Attack state (class='{cls}')", set_id=sid, transition=tr)
                if not after_attack_hint_added and not has_attack_state:
                    add('warning', f"set '{sid}': uses 'after_attack' — ensure runtime provides fsm.context['attack_duration'] and AttackState sets 'attack_start'", set_id=sid)
                    after_attack_hint_added = True
        except Exception:
            # Never block linting on best-effort semantic checks
            pass
        # Node-level initial flags consistency (optional)
        flagged = [st.get("id") for st in states if isinstance(st, dict) and st.get("initial") is True and isinstance(st.get("id"), str)]
        if len(flagged) > 1:
            add('warning', f"set '{sid}': multiple states flagged initial (using set.initial='{initial}')", set_id=sid)
        elif len(flagged) == 1 and isinstance(initial, str) and flagged[0] != initial:
            add('warning', f"set '{sid}': state '{flagged[0]}' flagged initial but set.initial is '{initial}'", set_id=sid, state_id=flagged[0])
        # States without outgoing transitions (only if no global transitions exist)
        try:
            if not has_global_transition:
                terminal_ids: set[str] = set()
                external_ids2: set[str] = set()
                for st in states:
                    nid = st.get("id")
                    if not isinstance(nid, str):
                        continue
                    if st.get("terminal") is True:
                        terminal_ids.add(nid)
                    if st.get("external_entry") is True:
                        external_ids2.add(nid)
                    spec = st.get("special")
                    if isinstance(spec, str) and spec.lower() in ("damage", "external", "interrupt", "alert"):
                        external_ids2.add(nid)
                from_counts: dict[str, int] = {nid: 0 for nid in state_ids}
                for tr in transitions:
                    fr2 = tr.get("from")
                    if isinstance(fr2, str) and fr2 in from_counts:
                        from_counts[fr2] += 1
                for nid in state_ids:
                    if nid in terminal_ids or nid in external_ids2:
                        continue
                    if from_counts.get(nid, 0) == 0:
                        add('warning', f"set '{sid}': state '{nid}' has no outgoing transitions (and no global transitions present)", set_id=sid, state_id=nid)
        except Exception:
            pass
        # Unreachable states from initial (warning only)
        if isinstance(initial, str) and initial in seen_state_ids:
            adj: dict[str, list[str]] = {nid: [] for nid in state_ids}
            # Build adjacency including global transitions
            global_targets: list[str] = []
            for tr in transitions:
                fr = tr.get("from"); to = tr.get("to")
                if not (isinstance(to, str) and to in adj):
                    continue
                if isinstance(fr, str) and fr == "*":
                    global_targets.append(to)
                elif isinstance(fr, str) and fr in adj:
                    adj[fr].append(to)
            if global_targets:
                for nid in state_ids:
                    adj[nid].extend(global_targets)
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
                add('warning', f"set '{sid}': unreachable states from initial: {filtered_unreachable}", set_id=sid)
    return warns, errs, enriched


def _export_ids_json(data: Dict[str, Any], out_path: str | Path | None = None) -> None:
    """Export an ids index JSON under data/fsm/ with keys SET_IDS, STATES_BY_SET, TRANSITIONS_BY_SET."""
    import json
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


__all__ = [
    "default_sets_path",
    "default_assignments_path",
    "default_layouts_path",
    "default_schema_path",
    "default_animation_map_path",
    "default_ids_path",
    "load_sets",
    "save_sets",
    "get_last_lint",
    "get_last_lint_enriched",
    "load_assignments",
    "save_assignments",
    "load_animation_map",
    "save_animation_map",
    "load_layouts",
    "save_layouts",
    "validate",
    "load_all",
]
