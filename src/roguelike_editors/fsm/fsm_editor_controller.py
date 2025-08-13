"""FSM Editor - Main Controller (skeleton)

Orchestrates panels (title, toolbar, sets, graph, properties), global state,
persistence hooks, history, and runtime reload bridge.
"""
from __future__ import annotations
from typing import Optional
import pygame

from .fsm_toolbar.fsm_toolbar_controller import FsmToolbarController
from .fsm_sets_panel.fsm_sets_panel_controller import FsmSetsPanelController
from .fsm_graph_panel.fsm_graph_panel_controller import FsmGraphPanelController
from roguelike_editors.fsm.services.fsm_persistence import (
    default_layouts_path,
    load_layouts,
)
from roguelike_editors.fsm.services.fsm_runtime_bridge import get_snapshot

class FsmEditorController:
    def __init__(self) -> None:
        # Visibility toggled by F12 elsewhere (FMSEventSpy/FMSController integration)
        self.visible: bool = False

        # Lazy-created/plugged submodules. Wired in later phases.
        self.title_controller = None
        self.toolbar_controller: Optional[FsmToolbarController] = FsmToolbarController()
        self.sets_panel_controller = FsmSetsPanelController()
        self.graph_panel_controller: Optional[FsmGraphPanelController] = FsmGraphPanelController()
        self.properties_panel_controller = None

        # View/Event handler can be split; keep placeholders for now
        self.view = None
        self.events = None

    # --- Lifecycle ---
    def render(self, screen) -> None:
        if not self.visible:
            return
        # Toolbar (left column, anchored). Returns its rect.
        toolbar_rect = None
        if self.toolbar_controller:
            try:
                toolbar_rect = self.toolbar_controller.render(screen)
            except Exception:
                toolbar_rect = None
        # Toggle Sets Panel by active tool
        tool = None
        try:
            tool = getattr(self.toolbar_controller.model, 'active_tool', None) if self.toolbar_controller else None
        except Exception:
            tool = None
        sets_rect = None
        if self.sets_panel_controller:
            try:
                self.sets_panel_controller.model.visible = (tool == 'sets')
                if self.sets_panel_controller.model.visible:
                    # Populate items from runtime snapshot
                    snap = get_snapshot()
                    set_ids = [s.get('id', '?') for s in snap.get('sets', [])]
                    self.sets_panel_controller.model.items = set_ids
                    # Compute anchor aligned next to toolbar
                    anchor = (20, 120)
                    try:
                        if toolbar_rect is not None:
                            margin = 8
                            # Default panel size must match view's temp rect (300x240)
                            panel_w, panel_h = 300, 240
                            sw, sh = screen.get_size()
                            ax = toolbar_rect.right + margin
                            ay = toolbar_rect.top
                            # Clamp to screen
                            ax = max(4, min(ax, max(4, sw - panel_w - 4)))
                            ay = max(4, min(ay, max(4, sh - panel_h - 4)))
                            anchor = (ax, ay)
                    except Exception:
                        pass
                    sets_rect = self.sets_panel_controller.render(screen, anchor=anchor)
            except Exception:
                pass
        # Graph panel to the right of the Sets panel when an item is selected
        if self.graph_panel_controller:
            try:
                # Only visible when sets tool active and a set selected
                selected_idx = None
                if self.sets_panel_controller and getattr(self.sets_panel_controller.model, 'visible', False):
                    selected_idx = getattr(self.sets_panel_controller.model, 'selected_index', None)
                self.graph_panel_controller.model.visible = (selected_idx is not None)
                if self.graph_panel_controller.model.visible and selected_idx is not None:
                    # Determine selected set id
                    items = getattr(self.sets_panel_controller.model, 'items', []) if self.sets_panel_controller else []
                    if 0 <= int(selected_idx) < len(items):
                        set_id = items[int(selected_idx)]
                    else:
                        set_id = None
                    # If changed, rebuild nodes/edges from snapshot
                    if set_id and self.graph_panel_controller.model.selected_set_id != set_id:
                        snap = get_snapshot()
                        set_def = None
                        try:
                            by_id = {s.get('id'): s for s in snap.get('sets', [])}
                            set_def = by_id.get(set_id)
                        except Exception:
                            set_def = None
                        nodes = []
                        edges = []
                        if set_def:
                            # Build nodes from states; grid layout in canvas space (800x520)
                            states = set_def.get('states', [])
                            transitions = set_def.get('transitions', [])
                            initial = set_def.get('initial')
                            n = max(1, len(states))
                            # Grid parameters
                            cols = max(1, int((n) ** 0.5))
                            rows = max(1, (n + cols - 1) // cols)
                            canvas_w, canvas_h = 800, 520
                            margin_x, margin_y = 40, 40
                            avail_w = max(100, canvas_w - margin_x * 2)
                            avail_h = max(80, canvas_h - margin_y * 2)
                            cell_w = max(120, avail_w // max(1, cols))
                            cell_h = max(80, avail_h // max(1, rows))
                            node_w, node_h = min(160, cell_w - 20), min(60, cell_h - 20)
                            # Stagger + jitter to avoid perfect alignments
                            import math, random
                            rng = random.Random((hash(set_def.get('id', 'set')) ^ len(states)) & 0xFFFFFFFF)
                            jitter_px = 8
                            stagger_x_ratio = 0.15
                            stagger_y_ratio = 0.10
                            for idx, s in enumerate(states):
                                r = idx // cols
                                c = idx % cols
                                base_x = margin_x + c * cell_w + (cell_w - node_w) // 2
                                base_y = margin_y + r * cell_h + (cell_h - node_h) // 2
                                # Stagger by row/column
                                stag_x = int((r % 2) * node_w * stagger_x_ratio)
                                stag_y = int((c % 2) * node_h * stagger_y_ratio)
                                # Deterministic small jitter
                                jx = rng.randint(-jitter_px, jitter_px)
                                jy = rng.randint(-jitter_px, jitter_px)
                                x = base_x + stag_x + jx
                                y = base_y + stag_y + jy
                                nodes.append({
                                    'id': s.get('id', f'n{idx}'),
                                    'label': s.get('id', f'n{idx}'),
                                    'x': int(x), 'y': int(y), 'w': int(node_w), 'h': int(node_h),
                                    'initial': s.get('id') == initial,
                                })
                            # Lightweight force-directed refinement (single-shot) to reduce crossings
                            if len(nodes) > 1:
                                id_to_idx = {n['id']: i for i, n in enumerate(nodes)}
                                # Build undirected edge list for attraction
                                undirected = set()
                                for t in transitions:
                                    fr = t.get('from'); to = t.get('to')
                                    if fr in id_to_idx and to in id_to_idx and fr != to:
                                        key = tuple(sorted((fr, to)))
                                        undirected.add(key)
                                undirected = list(undirected)
                                # Positions as floats
                                px = [float(n['x']) for n in nodes]
                                py = [float(n['y']) for n in nodes]
                                # Constants
                                area = float(avail_w * avail_h)
                                k = 0.45 * math.sqrt(area / max(1, len(nodes)))
                                iterations = min(25, 5 + len(nodes))
                                temperature = max(node_w, node_h)
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
                                        # Clamp inside margins
                                        px[i] = max(margin_x, min(px[i], canvas_w - margin_x - n['w']))
                                        py[i] = max(margin_y, min(py[i], canvas_h - margin_y - n['h']))
                                    temperature = max(0.0, temperature - cool)
                                # Write back
                                for i, n in enumerate(nodes):
                                    n['x'] = int(px[i])
                                    n['y'] = int(py[i])
                            # Override with persisted positions if present
                            try:
                                layouts = load_layouts(default_layouts_path())
                            except FileNotFoundError:
                                layouts = {"by_set": {}}
                            if isinstance(layouts, dict):
                                by_set = layouts.get("by_set") or {}
                                if isinstance(by_set, dict):
                                    entry = by_set.get(set_id) or {}
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
                            # Edges from transitions (carry label and optional style)
                            for t in transitions:
                                fr = t.get('from'); to = t.get('to')
                                if fr and to:
                                    e = {'from': fr, 'to': to}
                                    # Label from 'when'
                                    if 'when' in t:
                                        e['label'] = t.get('when')
                                    # Optional styling directly on transition
                                    for k in ('color', 'width', 'head_len', 'head_width', 'curved', 'curve_step', 'active'):
                                        if k in t:
                                            e[k] = t[k]
                                    # Or nested style dict
                                    style = t.get('style') or {}
                                    if isinstance(style, dict):
                                        e.update(style)
                                    edges.append(e)
                        self.graph_panel_controller.model.selected_set_id = set_id
                        self.graph_panel_controller.model.nodes = nodes
                        self.graph_panel_controller.model.edges = edges
                    # Compute anchor to the right of sets panel
                    g_anchor = (360, 120)
                    try:
                        if sets_rect is not None:
                            margin = 8
                            canvas_w, canvas_h = 800, 520
                            sw, sh = screen.get_size()
                            ax = sets_rect.right + margin
                            ay = sets_rect.top
                            ax = max(4, min(ax, max(4, sw - canvas_w - 4)))
                            ay = max(4, min(ay, max(4, sh - canvas_h - 4)))
                            g_anchor = (ax, ay)
                    except Exception:
                        pass
                    self.graph_panel_controller.render(screen, anchor=g_anchor)
            except Exception:
                pass
        # TODO: layout title -> toolbar -> left/center/right panels
        # No-op: Title rendering may be handled by a dedicated Title controller/view later
        return

    def handle_event(self, event) -> bool:
        if not self.visible:
            return False
        # Toolbar first, so drag/clicks don't leak to canvas
        if self.toolbar_controller and self.toolbar_controller.handle_event(event):
            return True
        # Sets panel events if visible
        try:
            if self.sets_panel_controller and getattr(self.sets_panel_controller.model, 'visible', False):
                if self.sets_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # Graph panel events if visible
        try:
            if self.graph_panel_controller and getattr(self.graph_panel_controller.model, 'visible', False):
                if self.graph_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # TODO: delegate to graph/properties event handlers next
        return False

    # --- Visibility ---
    def toggle_visible(self, flag: Optional[bool] = None) -> None:
        if flag is None:
            self.visible = not self.visible
        else:
            self.visible = bool(flag)


__all__ = ["FsmEditorController"]
