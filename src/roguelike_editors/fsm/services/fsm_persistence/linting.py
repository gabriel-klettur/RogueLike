from __future__ import annotations
from typing import Any, Dict, Tuple, List


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
