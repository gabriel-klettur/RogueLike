from __future__ import annotations

from typing import Any, Dict, List, Tuple
import math
import random

from roguelike_editors.fsm.services.fsm_persistence.fsm_persistence import (
    default_layouts_path,
    load_layouts,
)

CanvasSize = Tuple[int, int]


def _grid_layout_nodes(states: List[Dict[str, Any]], *, canvas: CanvasSize) -> List[Dict[str, Any]]:
    canvas_w, canvas_h = int(canvas[0]), int(canvas[1])
    n = max(1, len(states))
    cols = max(1, int(n ** 0.5))
    rows = max(1, (n + cols - 1) // cols)
    margin_x, margin_y = 40, 40
    avail_w = max(100, canvas_w - margin_x * 2)
    avail_h = max(80, canvas_h - margin_y * 2)
    cell_w = max(120, avail_w // max(1, cols))
    cell_h = max(80, avail_h // max(1, rows))
    node_w, node_h = min(160, cell_w - 20), min(60, cell_h - 20)

    rng = random.Random((len(states) << 8) ^ 0xA5A5_1234)
    jitter_px = 8
    stagger_x_ratio = 0.15
    stagger_y_ratio = 0.10

    nodes: List[Dict[str, Any]] = []
    for idx, s in enumerate(states):
        r = idx // cols
        c = idx % cols
        base_x = margin_x + c * cell_w + (cell_w - node_w) // 2
        base_y = margin_y + r * cell_h + (cell_h - node_h) // 2
        stag_x = int((r % 2) * node_w * stagger_x_ratio)
        stag_y = int((c % 2) * node_h * stagger_y_ratio)
        jx = rng.randint(-jitter_px, jitter_px)
        jy = rng.randint(-jitter_px, jitter_px)
        x = base_x + stag_x + jx
        y = base_y + stag_y + jy
        nodes.append({
            'id': s.get('id', f'n{idx}'),
            'label': s.get('id', f'n{idx}'),
            'x': int(x), 'y': int(y), 'w': int(node_w), 'h': int(node_h),
            'initial': False,  # to be set by caller using 'initial' id
        })
    return nodes


def _refine_positions(nodes: List[Dict[str, Any]], transitions: List[Dict[str, Any]], *, canvas: CanvasSize) -> None:
    if len(nodes) <= 1:
        return
    canvas_w, canvas_h = int(canvas[0]), int(canvas[1])
    margin_x, margin_y = 40, 40

    id_to_idx = {n['id']: i for i, n in enumerate(nodes)}
    undirected = set()
    for t in transitions:
        fr = t.get('from'); to = t.get('to')
        if fr in id_to_idx and to in id_to_idx and fr != to:
            key = tuple(sorted((fr, to)))
            undirected.add(key)
    undirected = list(undirected)

    px = [float(n['x']) for n in nodes]
    py = [float(n['y']) for n in nodes]

    area = float(max(1, (canvas_w - 2 * margin_x) * (canvas_h - 2 * margin_y)))
    k = 0.45 * math.sqrt(area / max(1, len(nodes)))
    iterations = min(25, 5 + len(nodes))
    temperature = max(max(n['w'] for n in nodes), max(n['h'] for n in nodes))
    cool = temperature / max(1, iterations)

    for _ in range(iterations):
        disp_x = [0.0] * len(nodes)
        disp_y = [0.0] * len(nodes)
        # Repulsion
        for i in range(len(nodes)):
            for j in range(i + 1, len(nodes)):
                dx = px[i] - px[j]
                dy = py[i] - py[j]
                dist = math.hypot(dx, dy) or 0.001
                force = (k * k) / dist
                ux, uy = dx / dist, dy / dist
                disp_x[i] += ux * force
                disp_y[i] += uy * force
                disp_x[j] -= ux * force
                disp_y[j] -= uy * force
        # Attraction
        for (a, b) in undirected:
            i = id_to_idx[a]; j = id_to_idx[b]
            dx = px[i] - px[j]
            dy = py[i] - py[j]
            dist = math.hypot(dx, dy) or 0.001
            force = (dist * dist) / k
            ux, uy = dx / dist, dy / dist
            disp_x[i] -= ux * force
            disp_y[i] -= uy * force
            disp_x[j] += ux * force
            disp_y[j] += uy * force
        # Apply with cooling and clamp to canvas
        for i, n in enumerate(nodes):
            dx = disp_x[i]; dy = disp_y[i]
            d = math.hypot(dx, dy) or 1.0
            step = min(temperature, d)
            px[i] += (dx / d) * step
            py[i] += (dy / d) * step
            px[i] = max(margin_x, min(px[i], canvas_w - margin_x - n['w']))
            py[i] = max(margin_y, min(py[i], canvas_h - margin_y - n['h']))
        temperature = max(0.0, temperature - cool)

    for i, n in enumerate(nodes):
        n['x'] = int(px[i])
        n['y'] = int(py[i])


def _build_edges(transitions: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    edges: List[Dict[str, Any]] = []
    for t in transitions:
        fr = t.get('from'); to = t.get('to')
        if fr and to:
            e = {'from': fr, 'to': to}
            if 'when' in t:
                e['label'] = t.get('when')
            for k in ('color', 'width', 'head_len', 'head_width', 'curved', 'curve_step', 'active'):
                if k in t:
                    e[k] = t[k]
            style = t.get('style') or {}
            if isinstance(style, dict):
                e.update(style)
            edges.append(e)
    return edges


def _apply_persisted_layouts(set_id: str, nodes: List[Dict[str, Any]], model: Any) -> None:
    try:
        layouts = load_layouts(default_layouts_path())
    except FileNotFoundError:
        layouts = {"by_set": {}}
    if not isinstance(layouts, dict):
        return
    by_set = layouts.get("by_set") or {}
    if not isinstance(by_set, dict):
        return
    entry = by_set.get(set_id) or {}
    if not isinstance(entry, dict):
        return
    nodes_map = entry.get("nodes") or {}
    if isinstance(nodes_map, dict):
        for n in nodes:
            saved = nodes_map.get(n.get('id'))
            if isinstance(saved, dict) and 'x' in saved and 'y' in saved:
                try:
                    n['x'] = int(saved['x'])
                    n['y'] = int(saved['y'])
                except Exception:
                    pass
    vp = entry.get("viewport") or {}
    if isinstance(vp, dict):
        try:
            z = float(vp.get("zoom", 1.0))
            model.zoom = max(0.2, min(3.0, z))
        except Exception:
            pass
        try:
            px = float(vp.get("pan_x", 0.0))
            py = float(vp.get("pan_y", 0.0))
            model.pan_x = px
            model.pan_y = py
        except Exception:
            pass
        try:
            lc = bool(vp.get("legend_collapsed", False))
            model.legend_collapsed = lc
        except Exception:
            pass


def build_graph_from_set(set_def: Dict[str, Any] | None, model: Any, *, canvas: CanvasSize = (800, 520)) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    """Construct nodes and edges for a given set definition.
    Applies a grid layout with a small refinement pass and persisted overrides.
    Also updates model viewport fields if present in persistence.
    """
    nodes: List[Dict[str, Any]] = []
    edges: List[Dict[str, Any]] = []
    if not set_def:
        return nodes, edges

    states = list(set_def.get('states', []))
    transitions = list(set_def.get('transitions', []))
    initial = set_def.get('initial')

    nodes = _grid_layout_nodes(states, canvas=canvas)
    # mark initial
    for n in nodes:
        n['initial'] = (n.get('id') == initial)

    _refine_positions(nodes, transitions, canvas=canvas)
    edges = _build_edges(transitions)

    # persisted overrides
    set_id = set_def.get('id') or set_def.get('name')
    if isinstance(set_id, str):
        _apply_persisted_layouts(set_id, nodes, model)

    return nodes, edges
