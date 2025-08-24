from __future__ import annotations

from typing import Any, Dict

from roguelike_editors.fsm.services.fsm_persistence import (
    default_layouts_path,
    load_layouts,
    save_layouts,
    default_sets_path,
    load_sets,
    save_sets,
)
from roguelike_editors.fsm.services.fsm_runtime_bridge import publish_reload


def persist_layout(model: Any) -> None:
    """Persist node positions and viewport state for the currently selected set.

    Writes into layouts.json at the path provided by fsm_persistence.default_layouts_path().
    """
    set_id = getattr(model, 'selected_set_id', None)
    if not set_id:
        return

    nodes = getattr(model, 'nodes', [])
    path = default_layouts_path()

    try:
        layouts: Dict[str, Any] = load_layouts(path)
    except FileNotFoundError:
        layouts = {"by_set": {}}

    if not isinstance(layouts, dict):
        layouts = {"by_set": {}}

    by_set = layouts.get("by_set")
    if not isinstance(by_set, dict):
        by_set = {}

    entry = by_set.get(set_id) or {}

    # Build nodes map
    nodes_map: Dict[str, Dict[str, int]] = {}
    for n in nodes:
        nid = n.get('id')
        if not nid:
            continue
        try:
            x = int(n.get('x', 0))
            y = int(n.get('y', 0))
        except Exception:
            continue
        nodes_map[nid] = {"x": x, "y": y}

    # Persist viewport (zoom, pan, legend)
    try:
        zoom = float(getattr(model, 'zoom', 1.0))
    except Exception:
        zoom = 1.0
    try:
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
    except Exception:
        pan_x, pan_y = 0.0, 0.0

    entry["nodes"] = nodes_map
    entry["viewport"] = {
        "zoom": zoom,
        "pan_x": pan_x,
        "pan_y": pan_y,
        "legend_collapsed": bool(getattr(model, 'legend_collapsed', False)),
    }

    by_set[set_id] = entry
    layouts["by_set"] = by_set
    save_layouts(layouts, path)


def persist_sets_structural(model: Any) -> None:
    """Persist structural FSM data (states, initial, transitions) for the selected set.

    - States are derived from current nodes.
    - Initial is taken from a node with `initial` flag if present; otherwise preserved or first state.
    - Transitions are rebuilt from edges, preserving `when` if possible.
    Triggers runtime hot-reload via publish_reload().
    """
    set_id = getattr(model, 'selected_set_id', None)
    if not set_id:
        return

    path = default_sets_path()
    data = load_sets(path)
    sets = (data or {}).get('sets') or []

    target = None
    for s in sets:
        if s.get('id') == set_id:
            target = s
            break
    if target is None:
        return

    # States
    existing_states = {st.get('id'): st for st in (target.get('states') or []) if isinstance(st, dict)}
    new_states = []
    initial_node_id = None

    for n in getattr(model, 'nodes', []):
        nid = n.get('id')
        if not nid:
            continue
        st = dict(existing_states.get(nid) or {'id': nid})
        # Keep label if present, else from node
        if 'label' not in st or not st.get('label'):
            if n.get('label'):
                st['label'] = n.get('label')
        # Flags
        if n.get('initial'):
            initial_node_id = nid
        st['terminal'] = bool(n.get('terminal', st.get('terminal', False)))
        new_states.append(st)

    target['states'] = new_states

    # Initial selection
    new_ids = [st.get('id') for st in new_states if isinstance(st.get('id'), str)]
    if initial_node_id:
        target['initial'] = initial_node_id
    else:
        prev_init = target.get('initial')
        if prev_init not in new_ids:
            if new_ids:
                target['initial'] = new_ids[0]

    # Transitions: build from edges, preserve existing 'when' when possible
    existing_trs = target.get('transitions') or []
    by_pair = {}
    for tr in existing_trs:
        key = (tr.get('from'), tr.get('to'))
        by_pair.setdefault(key, []).append(tr)

    new_trs = []
    for e in getattr(model, 'edges', []):
        fr = e.get('from'); to = e.get('to')
        if not fr or not to:
            continue
        key = (fr, to)
        carry = (by_pair.get(key) or [None])[0]
        when = e.get('label') if isinstance(e.get('label'), str) else (carry.get('when') if isinstance(carry, dict) else '')
        tr = {'from': fr, 'to': to, 'when': when}
        # Preserve optional style/fields
        if isinstance(carry, dict):
            for k in ('conditions', 'actions', 'style', 'color', 'width', 'head_len', 'head_width', 'curved', 'curve_step', 'active'):
                if k in carry and k not in tr:
                    tr[k] = carry[k]
        for k in ('color', 'width', 'head_len', 'head_width', 'curved', 'curve_step', 'active'):
            if k in e:
                tr[k] = e[k]
        new_trs.append(tr)

    target['transitions'] = new_trs

    # Save and hot-reload
    save_sets(data, path)
    try:
        publish_reload()
    except Exception:
        pass
