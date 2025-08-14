from __future__ import annotations
from typing import Optional

from .fsm_graph_panel_model import FsmGraphPanelModel
from .fsm_graph_panel_view import FsmGraphPanelView
from roguelike_editors.fsm.services.fsm_persistence import (
    default_layouts_path,
    load_layouts,
    save_layouts,
)
from roguelike_editors.fsm.services.fsm_id import new_id


class FsmGraphPanelController:
    def __init__(self, model: Optional[FsmGraphPanelModel] = None, view: Optional[FsmGraphPanelView] = None) -> None:
        self.model = model or FsmGraphPanelModel()
        self.view = view or FsmGraphPanelView()

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

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
        # Always consume wheel if inside (prevents game scroll under)
        if not inside:
            return False

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

        # Handle clicks on the graph toolbar buttons (top row inside canvas)
        if et == pygame.MOUSEBUTTONDOWN and btn == 1:
            try:
                tb_rects = getattr(self.view, 'graph_toolbar_rects', {}) or {}
                for tool_key, tb_rect in tb_rects.items():
                    if tb_rect.collidepoint(mouse_pos):
                        if tool_key in ('select', 'add_node', 'clone_node', 'connect', 'disconnect', 'delete', 'mark_ini', 'mark_end'):
                            self.model.active_graph_tool = tool_key
                        elif tool_key in ('zoom_in', 'zoom_out'):
                            factor = 1.1 if tool_key == 'zoom_in' else (1/1.1)
                            old_z = max(0.05, float(getattr(self.model, 'zoom', 1.0)))
                            new_z = max(0.2, min(3.0, old_z * factor))
                            if abs(new_z - old_z) > 1e-6:
                                # Zoom around canvas center
                                cx = rect.left + rect.w // 2
                                cy = rect.top + rect.h // 2
                                lcx, lcy = to_local((cx, cy))
                                wx, wy = to_world(lcx, lcy)
                                self.model.zoom = new_z
                                self.model.pan_x = lcx - wx * new_z
                                self.model.pan_y = lcy - wy * new_z
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
                            if src != nid:
                                # add edge if not existing
                                exists = any((e.get('from') == src and e.get('to') == nid) for e in getattr(self.model, 'edges', []))
                                if not exists:
                                    self.model.edges.append({'from': src, 'to': nid})
                            self.model.connect_source_node_id = None
                        return True
                    # click on empty: cancel pending connection
                    self.model.connect_source_node_id = None
                    return True
                elif tool == 'disconnect':
                    if node is not None:
                        nid = node.get('id')
                        src = getattr(self.model, 'connect_source_node_id', None)
                        if not src:
                            self.model.connect_source_node_id = nid
                        else:
                            if src != nid:
                                # remove edge if present
                                self.model.edges = [
                                    e for e in getattr(self.model, 'edges', [])
                                    if not (e.get('from') == src and e.get('to') == nid)
                                ]
                            self.model.connect_source_node_id = None
                        return True
                    # click on empty: cancel pending disconnect
                    self.model.connect_source_node_id = None
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
                            # Mark terminal/end flag on this node
                            for n in getattr(self.model, 'nodes', []):
                                if n.get('id') == nid:
                                    n['terminal'] = True
                        return True
                    return True
            if btn in (2, 3):  # middle or right button pans
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
            if btn in (2, 3) and self.model.dragging_pan:
                self.model.dragging_pan = False
                return True

        if et == pygame.MOUSEMOTION:
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
                self.model.pan_x = pan_x + dx
                self.model.pan_y = pan_y + dy
                self.model.drag_last_local_x = int(local_x)
                self.model.drag_last_local_y = int(local_y)
                return True
            # hover could be added here later
            return True  # consume motion in canvas

        if et == pygame.MOUSEWHEEL:
            # zoom in/out around mouse position
            factor = 1.1 ** getattr(event, 'y', 0)
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
        entry["nodes"] = nodes_map
        by_set[set_id] = entry
        layouts["by_set"] = by_set
        save_layouts(layouts, path)


__all__ = ["FsmGraphPanelController"]
