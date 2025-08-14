from __future__ import annotations
from typing import Optional
import logging

from .fsm_graph_panel_model import FsmGraphPanelModel
from .fsm_graph_panel_view import FsmGraphPanelView
from .toolbar_graph_panel.toolbar_graph_panel_controller import FsmGraphToolbarController
from .toolbar_graph_panel.toolbar_graph_panel_events import FsmGraphToolbarEventHandler
from roguelike_editors.fsm.services.fsm_persistence import (
    default_layouts_path,
    load_layouts,
    save_layouts,
)
from roguelike_editors.fsm.services.fsm_id import new_id

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.controller")


class FsmGraphPanelController:
    def __init__(self, model: Optional[FsmGraphPanelModel] = None, view: Optional[FsmGraphPanelView] = None) -> None:
        self.model = model or FsmGraphPanelModel()
        self.view = view or FsmGraphPanelView()
        # Dedicated toolbar MVC for graph tools
        self.toolbar = FsmGraphToolbarController()
        self.toolbar_events = FsmGraphToolbarEventHandler()

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen, toolbar=self.toolbar)
        return self.view.render(self.model, screen, anchor=anchor, toolbar=self.toolbar)

    def handle_event(self, event) -> bool:
        # Interactive graph canvas: pan/zoom, select/drag nodes
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        if not getattr(self.model, 'visible', False):
            return False
        rect = getattr(self.view, 'canvas_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        mouse_pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        inside = rect.collidepoint(mouse_pos)
        # Only ignore events outside the canvas if we are NOT currently dragging (pan or node)
        if (
            not inside
            and not getattr(self.model, 'dragging_pan', False)
            and getattr(self.model, 'dragging_node_id', None) is None
        ):
            if et == pygame.MOUSEWHEEL:
                LOGGER.debug("[GraphPanel][WHEEL IGNORED] outside canvas. mouse=%s rect=%s", mouse_pos, rect)
            return False

        # Safety: if we are in pan mode but middle button is no longer physically pressed, force release
        try:
            try:
                buttons = pygame.mouse.get_pressed(5)  # prefer CE signature
            except TypeError:
                buttons = pygame.mouse.get_pressed()
            mid_down = bool(buttons[1]) if buttons and len(buttons) > 1 else False
            left_down = bool(buttons[0]) if buttons and len(buttons) > 0 else False
        except Exception:
            mid_down = True  # Don't force release if we can't read state
            left_down = True
        if getattr(self.model, 'dragging_pan', False) and not mid_down:
            LOGGER.debug("[GraphPanel][PAN FORCE-RELEASE] middle not pressed anymore; ending drag.")
            self.model.dragging_pan = False
        if getattr(self.model, 'dragging_node_id', None) is not None and not left_down:
            LOGGER.debug("[GraphPanel][NODE FORCE-RELEASE] left not pressed anymore; ending drag.")
            self.model.dragging_node_id = None
            # Persist layout after finishing a node drag
            try:
                self._persist_layout()
            except Exception:
                pass

        # Keyboard shortcuts for toolbar (+/- zoom) when mouse is over canvas
        try:
            if et == pygame.MOUSEWHEEL:
                LOGGER.debug("[GraphPanel][WHEEL] delegating to toolbar_events. mouse=%s inside=%s rect=%s", mouse_pos, inside, rect)
            if self.toolbar_events.handle_event(event, canvas_rect=rect, graph_model=self.model):
                if et == pygame.MOUSEWHEEL:
                    LOGGER.debug("[GraphPanel][WHEEL] handled by toolbar_events")
                # Persist viewport (zoom/pan) after toolbar-handled zoom
                try:
                    self._persist_layout()
                except Exception:
                    pass
                return True
        except Exception:
            pass

        # Helpers
        def to_local(p):
            return (p[0] - rect.left, p[1] - rect.top)

        def to_world(local_x, local_y):
            z = max(0.05, float(getattr(self.model, 'zoom', 1.0)))
            pan_x = float(getattr(self.model, 'pan_x', 0.0))
            pan_y = float(getattr(self.model, 'pan_y', 0.0))
            return ((local_x - pan_x) / z, (local_y - pan_y) / z)

        # Node hit-test in world coordinates
        def pick_node(wx, wy):
            for n in reversed(list(getattr(self.model, 'nodes', []))):  # top-most last, keep simple
                nx, ny = int(n.get('x', 0)), int(n.get('y', 0))
                nw, nh = int(n.get('w', 120)), int(n.get('h', 60))
                if nx <= wx <= nx + nw and ny <= wy <= ny + nh:
                    return n
            return None

        # State
        btn = getattr(event, 'button', None)
        local_x, local_y = to_local(mouse_pos)
        pan_x = float(getattr(self.model, 'pan_x', 0.0))
        pan_y = float(getattr(self.model, 'pan_y', 0.0))
        zoom = float(getattr(self.model, 'zoom', 1.0))

        # Handle clicks on the graph toolbar buttons via toolbar controller
        if et == pygame.MOUSEBUTTONDOWN and btn == 1:
            try:
                if self.toolbar.handle_mouse_down(mouse_pos, rect, self.model):
                    # Persist viewport/tool state after toolbar interaction (e.g., zoom)
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                    return True
            except Exception:
                pass

        if et == pygame.MOUSEBUTTONDOWN:
            if btn == 1:
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                tool = getattr(self.model, 'active_graph_tool', 'select')
                if tool == 'select':
                    if node is not None:
                        self.model.selected_node_id = node.get('id')
                        self.model.dragging_node_id = node.get('id')
                        self.model.drag_offset_x = node.get('x', 0) - wx
                        self.model.drag_offset_y = node.get('y', 0) - wy
                    else:
                        # deselect if clicking empty space
                        self.model.selected_node_id = None
                    return True
                elif tool == 'connect':
                    if node is not None:
                        nid = node.get('id')
                        src = getattr(self.model, 'connect_source_node_id', None)
                        if not src:
                            self.model.connect_source_node_id = nid
                        else:
                            # add edge if not existing
                            exists = any((e.get('from') == src and e.get('to') == nid) for e in getattr(self.model, 'edges', []))
                            if not exists:
                                self.model.edges.append({'from': src, 'to': nid})
                            self.model.connect_source_node_id = None
                        # Persist after connection interaction
                        try:
                            self._persist_layout()
                        except Exception:
                            pass
                        return True
                    # click on empty: cancel pending connection
                    self.model.connect_source_node_id = None
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                    return True
                elif tool == 'disconnect':
                    if node is not None:
                        nid = node.get('id')
                        src = getattr(self.model, 'connect_source_node_id', None)
                        if not src:
                            self.model.connect_source_node_id = nid
                        else:
                            # remove edge if present
                            self.model.edges = [
                                e for e in getattr(self.model, 'edges', [])
                                if not (e.get('from') == src and e.get('to') == nid)
                            ]
                            self.model.connect_source_node_id = None
                        # Persist after disconnect interaction
                        try:
                            self._persist_layout()
                        except Exception:
                            pass
                        return True
                    # click on empty: cancel pending disconnect
                    self.model.connect_source_node_id = None
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                    return True
                elif tool == 'delete':
                    if node is not None:
                        nid = node.get('id')
                        # remove node
                        self.model.nodes = [n for n in getattr(self.model, 'nodes', []) if n.get('id') != nid]
                        # remove connected edges
                        self.model.edges = [e for e in getattr(self.model, 'edges', []) if e.get('from') != nid and e.get('to') != nid]
                        if getattr(self.model, 'selected_node_id', None) == nid:
                            self.model.selected_node_id = None
                        # Persist after delete
                        try:
                            self._persist_layout()
                        except Exception:
                            pass
                        return True
                    return True
                elif tool == 'add_node':
                    if node is None:
                        # create a new node at click position
                        existing_ids = {n.get('id') for n in getattr(self.model, 'nodes', []) if isinstance(n.get('id'), str)}
                        nid = new_id('node', existing_ids)
                        self.model.nodes.append({
                            'id': nid,
                            'label': nid,
                            'x': int(wx), 'y': int(wy),
                            'w': 120, 'h': 60,
                        })
                        self.model.selected_node_id = nid
                        # Persist after add
                        try:
                            self._persist_layout()
                        except Exception:
                            pass
                        return True
                    return True
                elif tool == 'clone_node':
                    if node is not None:
                        existing_ids = {n.get('id') for n in getattr(self.model, 'nodes', []) if isinstance(n.get('id'), str)}
                        nid = new_id('node', existing_ids)
                        new_node = dict(node)
                        new_node['id'] = nid
                        new_node['x'] = int(node.get('x', 0)) + 20
                        new_node['y'] = int(node.get('y', 0)) + 20
                        # Reset flags that should not duplicate by default
                        if new_node.get('initial'):
                            new_node['initial'] = False
                        self.model.nodes.append(new_node)
                        self.model.selected_node_id = nid
                        # Persist after clone
                        try:
                            self._persist_layout()
                        except Exception:
                            pass
                        return True
                    return True
                elif tool in ('mark_ini', 'mark_end'):
                    if node is not None:
                        nid = node.get('id')
                        if tool == 'mark_ini':
                            # Clear existing and mark this one
                            for n in getattr(self.model, 'nodes', []):
                                n['initial'] = (n.get('id') == nid)
                        else:
                            # Toggle terminal/end flag on this node
                            for n in getattr(self.model, 'nodes', []):
                                if n.get('id') == nid:
                                    n['terminal'] = not bool(n.get('terminal'))
                        # Persist after mark
                        try:
                            self._persist_layout()
                        except Exception:
                            pass
                        return True
                    return True
            if btn == 2:  # middle button pans
                LOGGER.debug(
                    "[GraphPanel][PAN START] inside=%s mouse=%s local=(%d,%d) pan=(%s,%s) zoom=%.3f",
                    inside, mouse_pos, int(local_x), int(local_y),
                    getattr(self.model, 'pan_x', 0.0), getattr(self.model, 'pan_y', 0.0),
                    float(getattr(self.model, 'zoom', 1.0)),
                )
                self.model.dragging_pan = True
                self.model.drag_last_local_x = int(local_x)
                self.model.drag_last_local_y = int(local_y)
                return True

        if et == pygame.MOUSEBUTTONUP:
            if btn == 1 and self.model.dragging_node_id is not None:
                self.model.dragging_node_id = None
                # Persist layout after finishing a node drag
                try:
                    self._persist_layout()
                except Exception:
                    pass
                return True
            if btn == 2 and self.model.dragging_pan:
                LOGGER.debug(
                    "[GraphPanel][PAN END] mouse=%s local=(%d,%d) pan=(%s,%s)",
                    mouse_pos, int(local_x), int(local_y),
                    getattr(self.model, 'pan_x', 0.0), getattr(self.model, 'pan_y', 0.0),
                )
                self.model.dragging_pan = False
                # Persist viewport at end of pan
                try:
                    self._persist_layout()
                except Exception:
                    pass
                return True

        if et == pygame.MOUSEMOTION:
            # If the middle button is pressed while moving and we're not yet panning, start pan now
            try:
                try:
                    buttons = pygame.mouse.get_pressed(5)
                except TypeError:
                    buttons = pygame.mouse.get_pressed()
                mid_down_now = bool(buttons[1]) if buttons and len(buttons) > 1 else False
                left_down_now = bool(buttons[0]) if buttons and len(buttons) > 0 else False
            except Exception:
                mid_down_now = False
                left_down_now = False

            if mid_down_now and not getattr(self.model, 'dragging_pan', False) and inside:
                if getattr(self.model, 'dragging_node_id', None) is not None:
                    # Cancel node drag if any; middle-drag should always pan the canvas
                    self.model.dragging_node_id = None
                self.model.dragging_pan = True
                self.model.drag_last_local_x = int(local_x)
                self.model.drag_last_local_y = int(local_y)
                LOGGER.debug(
                    "[GraphPanel][PAN START@MOTION] mouse=%s local=(%d,%d) pan=(%s,%s) zoom=%.3f",
                    mouse_pos, int(local_x), int(local_y),
                    getattr(self.model, 'pan_x', 0.0), getattr(self.model, 'pan_y', 0.0),
                    float(getattr(self.model, 'zoom', 1.0)),
                )
                return True

            # If left is held while moving and we're not yet dragging a node, treat it as click-on-move
            # This makes clicks register even while the mouse is moving (e.g., quick click+move)
            if (
                left_down_now
                and getattr(self.model, 'dragging_node_id', None) is None
                and not getattr(self.model, 'dragging_pan', False)
                and getattr(self.model, 'active_graph_tool', 'select') == 'select'
                and inside
            ):
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                if node is not None:
                    self.model.selected_node_id = node.get('id')
                    self.model.dragging_node_id = node.get('id')
                    self.model.drag_offset_x = node.get('x', 0) - wx
                    self.model.drag_offset_y = node.get('y', 0) - wy
                    # Do not return; fall-through so the drag logic below moves the node immediately

            if self.model.dragging_node_id and getattr(self.model, 'active_graph_tool', 'select') == 'select':
                wx, wy = to_world(local_x, local_y)
                # mutate node position in world space
                nid = self.model.dragging_node_id
                for n in getattr(self.model, 'nodes', []):
                    if n.get('id') == nid:
                        n['x'] = int(wx + self.model.drag_offset_x)
                        n['y'] = int(wy + self.model.drag_offset_y)
                        break
                return True
            if self.model.dragging_pan:
                dx = int(local_x) - int(self.model.drag_last_local_x)
                dy = int(local_y) - int(self.model.drag_last_local_y)
                before = (getattr(self.model, 'pan_x', 0.0), getattr(self.model, 'pan_y', 0.0))
                self.model.pan_x = pan_x + dx
                self.model.pan_y = pan_y + dy
                LOGGER.debug(
                    "[GraphPanel][PAN MOVE] mouse=%s local=(%d,%d) dx=%d dy=%d pan %s -> (%s,%s)",
                    mouse_pos, int(local_x), int(local_y), dx, dy, before,
                    getattr(self.model, 'pan_x', 0.0), getattr(self.model, 'pan_y', 0.0),
                )
                self.model.drag_last_local_x = int(local_x)
                self.model.drag_last_local_y = int(local_y)
                return True
            # Hover tracking (highlight labels) when moving mouse over canvas
            try:
                # Node hover by node rect in world coords (not only label)
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                self.model.hover_node_id = node.get('id') if node is not None else None
            except Exception:
                self.model.hover_node_id = None

            try:
                # Edge hover: first try label rects; if not, use proximity to polyline path
                ex, ey = int(local_x), int(local_y)
                hover_e = None
                # 1) Label rects
                label_rects = getattr(self.view, 'edge_label_rects', {}) or {}
                for ei, r in label_rects.items():
                    try:
                        if r.collidepoint(ex, ey):
                            hover_e = ei
                            break
                    except Exception:
                        continue
                # 2) Proximity to edge path if no label hit
                if hover_e is None:
                    import math
                    paths = getattr(self.view, 'edge_paths', {}) or {}
                    best_e = None
                    best_d = 1e9
                    # helper: distance from point to segment
                    def _dist_pt_seg(px, py, ax, ay, bx, by):
                        vx, vy = bx - ax, by - ay
                        wx, wy = px - ax, py - ay
                        vv = vx*vx + vy*vy
                        if vv <= 1e-6:
                            dx, dy = px - ax, py - ay
                            return math.hypot(dx, dy)
                        t = max(0.0, min(1.0, (wx*vx + wy*vy) / vv))
                        cx, cy = ax + t*vx, ay + t*vy
                        return math.hypot(px - cx, py - cy)
                    for ei, pts in paths.items():
                        try:
                            if not pts or len(pts) < 2:
                                continue
                            # check each segment; early exit if under threshold
                            for i in range(len(pts)-1):
                                ax, ay = pts[i]
                                bx, by = pts[i+1]
                                d = _dist_pt_seg(ex, ey, ax, ay, bx, by)
                                if d < best_d:
                                    best_d = d
                                    best_e = ei
                        except Exception:
                            continue
                    # tolerance in pixels (local space)
                    tol = 8
                    hover_e = best_e if best_d <= tol else None
                self.model.hover_edge_index = hover_e
            except Exception:
                self.model.hover_edge_index = None

            return True  # consume motion in canvas

        # Fallback: some environments emit wheel as buttons 4/5
        if et == pygame.MOUSEBUTTONDOWN and btn in (4, 5):
            y = 1 if btn == 4 else -1
            factor = 1.1 ** y
            LOGGER.debug("[GraphPanel][WHEEL BTN] controller handling btn=%s y=%s mouse=%s factor=%.3f", btn, y, mouse_pos, factor)
            if factor != 1.0:
                old_z = max(0.05, zoom)
                new_z = max(0.2, min(3.0, old_z * factor))
                if abs(new_z - old_z) > 1e-6:
                    wx, wy = to_world(local_x, local_y)
                    self.model.zoom = new_z
                    self.model.pan_x = local_x - wx * new_z
                    self.model.pan_y = local_y - wy * new_z
                    LOGGER.debug("[GraphPanel][WHEEL BTN] updated zoom %.3f->%.3f pan=(%.1f,%.1f)", old_z, new_z, self.model.pan_x, self.model.pan_y)
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                return True

        if et == pygame.MOUSEWHEEL:
            # zoom in/out around mouse position
            y = getattr(event, 'y', 0)
            factor = 1.1 ** y
            LOGGER.debug("[GraphPanel][WHEEL] controller handling y=%s mouse=%s factor=%.3f", y, mouse_pos, factor)
            if factor != 1.0:
                old_z = max(0.05, zoom)
                new_z = max(0.2, min(3.0, old_z * factor))
                if abs(new_z - old_z) > 1e-6:
                    # world point under mouse before zoom
                    wx, wy = to_world(local_x, local_y)
                    # adjust pan so that mouse stays over same world point
                    self.model.zoom = new_z
                    self.model.pan_x = local_x - wx * new_z
                    self.model.pan_y = local_y - wy * new_z
                    LOGGER.debug("[GraphPanel][WHEEL] updated zoom %.3f->%.3f pan=(%.1f,%.1f)", old_z, new_z, self.model.pan_x, self.model.pan_y)
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                return True

        return True

    def _persist_layout(self) -> None:
        """Save current node positions for the selected set to layouts.json."""
        set_id = getattr(self.model, 'selected_set_id', None)
        if not set_id:
            return
        nodes = getattr(self.model, 'nodes', [])
        path = default_layouts_path()
        try:
            layouts = load_layouts(path)
        except FileNotFoundError:
            layouts = {"by_set": {}}
        if not isinstance(layouts, dict):
            layouts = {"by_set": {}}
        by_set = layouts.get("by_set")
        if not isinstance(by_set, dict):
            by_set = {}
        entry = by_set.get(set_id) or {}
        # Build nodes map
        nodes_map = {}
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
        # Persist viewport (zoom, pan)
        try:
            zoom = float(getattr(self.model, 'zoom', 1.0))
        except Exception:
            zoom = 1.0
        try:
            pan_x = float(getattr(self.model, 'pan_x', 0.0))
            pan_y = float(getattr(self.model, 'pan_y', 0.0))
        except Exception:
            pan_x, pan_y = 0.0, 0.0
        entry["nodes"] = nodes_map
        entry["viewport"] = {"zoom": zoom, "pan_x": pan_x, "pan_y": pan_y}
        by_set[set_id] = entry
        layouts["by_set"] = by_set
        save_layouts(layouts, path)


__all__ = ["FsmGraphPanelController"]
