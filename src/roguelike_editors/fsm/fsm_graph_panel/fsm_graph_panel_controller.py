from __future__ import annotations
from typing import Optional
import logging
import math

from .fsm_graph_panel_model import FsmGraphPanelModel
from .fsm_graph_panel_view import FsmGraphPanelView
from .toolbar_graph_panel.toolbar_graph_panel_controller import FsmGraphToolbarController
from .toolbar_graph_panel.toolbar_graph_panel_events import FsmGraphToolbarEventHandler
from roguelike_editors.fsm.services.fsm_persistence import (
    default_layouts_path,
    load_layouts,
    save_layouts,
    default_sets_path,
    load_sets,
    save_sets,
)
from roguelike_editors.fsm.services.fsm_runtime_bridge import publish_reload
from roguelike_editors.fsm.services.fsm_id import new_id
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.controller")


class FsmGraphPanelController:
    def __init__(self, model: Optional[FsmGraphPanelModel] = None, view: Optional[FsmGraphPanelView] = None) -> None:
        self.model = model or FsmGraphPanelModel()
        self.view = view or FsmGraphPanelView()
        # Dedicated toolbar MVC for graph tools
        self.toolbar = FsmGraphToolbarController()
        self.toolbar_events = FsmGraphToolbarEventHandler()
        # Double click support for label editing
        self._dbl = DoubleClickDetector()

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
        # Special-case: if inline text editing is active, allow committing with clicks anywhere on screen
        try:
            ti_active = bool(getattr(getattr(self.view, 'text_input', None), 'active', False))
        except Exception:
            ti_active = False
        if ti_active and et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            abs_r = getattr(self.view, 'text_input_abs_rect', None)
            if abs_r is None or not abs_r.collidepoint(mouse_pos):
                ti = getattr(self.view, 'text_input', None)
                try:
                    ti.deactivate()
                except Exception:
                    pass
                text = str(getattr(ti, 'text', '') or '')
                if getattr(self.model, 'editing_node_id', None):
                    nid = self.model.editing_node_id
                    for n in getattr(self.model, 'nodes', []):
                        if n.get('id') == nid:
                            n['label'] = text
                            break
                elif getattr(self.model, 'editing_edge_index', None) is not None:
                    try:
                        ei = int(self.model.editing_edge_index)  # type: ignore[arg-type]
                    except Exception:
                        ei = -1
                    edges = getattr(self.model, 'edges', [])
                    if isinstance(ei, int) and 0 <= ei < len(edges):
                        edges[ei]['label'] = text
                self.model.editing_node_id = None
                self.model.editing_edge_index = None
                self.model.editing_text = None
                try:
                    self._persist_sets_structural()
                except Exception:
                    pass
                try:
                    self._persist_layout()
                except Exception:
                    pass
                return True
        # Only ignore events outside the canvas if we are NOT currently dragging (pan or node)
        # and NOT inline-editing (text input should receive keys/clicks globally)
        if (
            not inside
            and not getattr(self.model, 'dragging_pan', False)
            and getattr(self.model, 'dragging_node_id', None) is None
            and getattr(self.model, 'dragging_edge_index', None) is None
            and not ti_active
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
        if getattr(self.model, 'dragging_edge_index', None) is not None and not left_down:
            LOGGER.debug("[GraphPanel][EDGE FORCE-RELEASE] left not pressed anymore; canceling edge handle drag.")
            # Cancel edge handle drag (no structural change on force-cancel)
            self.model.dragging_edge_index = None
            self.model.dragging_edge_end = None
            self.model.dragging_edge_preview_x = None
            self.model.dragging_edge_preview_y = None
            self.model.dragging_edge_orig_from = None
            self.model.dragging_edge_orig_to = None
            self.model.hover_edge_handle_end = None

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

        # Closest edge handle under local (canvas) coordinates
        def closest_handle(lx: float, ly: float, *, radius: int = 8):
            try:
                ends_map = getattr(self.view, 'edge_endpoints_local', {}) or {}
            except Exception:
                ends_map = {}
            best = None
            best_d2 = radius * radius
            for ei, ends in (ends_map.items() if isinstance(ends_map, dict) else []):
                try:
                    if not isinstance(ends, dict):
                        continue
                    for side in ('from', 'to'):
                        p = ends.get(side)
                        if not p:
                            continue
                        dx = float(lx) - float(p[0])
                        dy = float(ly) - float(p[1])
                        d2 = dx*dx + dy*dy
                        if d2 <= best_d2:
                            best = (int(ei), side)
                            best_d2 = d2
                except Exception:
                    continue
            return best

        # State
        btn = getattr(event, 'button', None)
        local_x, local_y = to_local(mouse_pos)
        pan_x = float(getattr(self.model, 'pan_x', 0.0))
        pan_y = float(getattr(self.model, 'pan_y', 0.0))
        zoom = float(getattr(self.model, 'zoom', 1.0))

        # Inline text editing active: delegate events to TextInput and swallow others
        try:
            ti = getattr(self.view, 'text_input', None)
        except Exception:
            ti = None
        if ti is not None and getattr(ti, 'active', False):
            # Cancel on ESC
            if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
                try:
                    ti.deactivate()
                except Exception:
                    pass
                self.model.editing_node_id = None
                self.model.editing_edge_index = None
                self.model.editing_text = None
                return True
            # Delegate to widget
            try:
                if et == pygame.MOUSEBUTTONDOWN:
                    # Adjust to local canvas coordinates so TextInput hit-testing works
                    try:
                        adj_event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {
                            'pos': (int(local_x), int(local_y)),
                            'button': getattr(event, 'button', None),
                        })
                    except Exception:
                        adj_event = event
                    handled = bool(ti.handle_event(adj_event))
                else:
                    handled = bool(ti.handle_event(event))
            except Exception:
                handled = False
            if handled:
                # Live-sync editing text for dynamic label rect sizing during typing
                try:
                    self.model.editing_text = str(getattr(ti, 'text', '') or '')
                except Exception:
                    pass
                # Commit when widget deactivates (Enter)
                if not getattr(ti, 'active', False):
                    text = str(getattr(ti, 'text', '') or '')
                    if getattr(self.model, 'editing_node_id', None):
                        nid = self.model.editing_node_id
                        for n in getattr(self.model, 'nodes', []):
                            if n.get('id') == nid:
                                n['label'] = text
                                break
                    elif getattr(self.model, 'editing_edge_index', None) is not None:
                        try:
                            ei = int(self.model.editing_edge_index)  # type: ignore[arg-type]
                        except Exception:
                            ei = -1
                        edges = getattr(self.model, 'edges', [])
                        if isinstance(ei, int) and 0 <= ei < len(edges):
                            edges[ei]['label'] = text
                    self.model.editing_node_id = None
                    self.model.editing_edge_index = None
                    self.model.editing_text = None
                    try:
                        self._persist_sets_structural()
                    except Exception:
                        pass
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                return True
            # Click outside input rectangle: commit and close
            if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                abs_r = getattr(self.view, 'text_input_abs_rect', None)
                if abs_r is None or not abs_r.collidepoint(mouse_pos):
                    try:
                        ti.deactivate()
                    except Exception:
                        pass
                    text = str(getattr(ti, 'text', '') or '')
                    if getattr(self.model, 'editing_node_id', None):
                        nid = self.model.editing_node_id
                        for n in getattr(self.model, 'nodes', []):
                            if n.get('id') == nid:
                                n['label'] = text
                                break
                    elif getattr(self.model, 'editing_edge_index', None) is not None:
                        try:
                            ei = int(self.model.editing_edge_index)  # type: ignore[arg-type]
                        except Exception:
                            ei = -1
                        edges = getattr(self.model, 'edges', [])
                        if isinstance(ei, int) and 0 <= ei < len(edges):
                            edges[ei]['label'] = text
                    self.model.editing_node_id = None
                    self.model.editing_edge_index = None
                    self.model.editing_text = None
                    try:
                        self._persist_sets_structural()
                    except Exception:
                        pass
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                    return True
            # Swallow other events while editing
            return True

        # Global ESC handling when not inline-editing: cancel drags or pending connect/disconnect
        try:
            if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
                # Cancel edge handle drag
                if getattr(self.model, 'dragging_edge_index', None) is not None:
                    LOGGER.debug("[GraphPanel][EDGE DRAG CANCEL] via ESC")
                    self.model.dragging_edge_index = None
                    self.model.dragging_edge_end = None
                    self.model.dragging_edge_preview_x = None
                    self.model.dragging_edge_preview_y = None
                    self.model.dragging_edge_orig_from = None
                    self.model.dragging_edge_orig_to = None
                    self.model.hover_edge_handle_end = None
                    return True
                # Cancel pending connect/disconnect source selection
                tool = getattr(self.model, 'active_graph_tool', 'select')
                if tool in ('connect', 'disconnect') and getattr(self.model, 'connect_source_node_id', None):
                    LOGGER.debug("[GraphPanel][%s CANCEL] via ESC", tool)
                    self.model.connect_source_node_id = None
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                    return True
        except Exception:
            pass

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

        # Legend minimize/expand toggle and click capture
        if et == pygame.MOUSEBUTTONDOWN and btn == 1:
            try:
                lbr = getattr(self.view, 'legend_button_rect', None)
                lrect = getattr(self.view, 'legend_rect', None)
                # Click on button toggles
                if lbr is not None and lbr.collidepoint(mouse_pos):
                    self.model.legend_collapsed = not bool(getattr(self.model, 'legend_collapsed', False))
                    try:
                        self._persist_layout()
                    except Exception:
                        pass
                    return True
                # Click inside legend body: consume; expand if collapsed
                if lrect is not None and lrect.collidepoint(mouse_pos):
                    if bool(getattr(self.model, 'legend_collapsed', False)):
                        self.model.legend_collapsed = False
                        try:
                            self._persist_layout()
                        except Exception:
                            pass
                    return True
            except Exception:
                pass

        if et == pygame.MOUSEBUTTONDOWN:
            if btn == 1:
                # Priority: label click/double-click handling for inline edit
                if inside:
                    try:
                        # Node label hit-test (use local coords)
                        nlrs = getattr(self.view, 'node_label_rects', {}) or {}
                        for nid, r in (nlrs.items() if isinstance(nlrs, dict) else []):
                            try:
                                if r.collidepoint(int(local_x), int(local_y)):
                                    # Double-click => begin edit; single-click => select only
                                    if self._dbl.is_double_click(('node_label', nid)):
                                        # Initialize edit state
                                        self.model.editing_node_id = nid
                                        self.model.editing_edge_index = None
                                        init = ''
                                        for n in getattr(self.model, 'nodes', []):
                                            if n.get('id') == nid:
                                                init = str(n.get('label', nid))
                                                break
                                        self.model.editing_text = init
                                        try:
                                            self.view.begin_text_edit(init, select_all=True)
                                        except Exception:
                                            pass
                                        return True
                                    else:
                                        self.model.selected_node_id = nid
                                        return True
                            except Exception:
                                continue
                    except Exception:
                        pass
                    try:
                        # Edge label hit-test
                        elrs = getattr(self.view, 'edge_label_rects', {}) or {}
                        for ei, r in (elrs.items() if isinstance(elrs, dict) else []):
                            try:
                                if r.collidepoint(int(local_x), int(local_y)):
                                    self.model.selected_edge_index = int(ei)
                                    if self._dbl.is_double_click(('edge_label', int(ei))):
                                        self.model.editing_node_id = None
                                        try:
                                            ei_int = int(ei)
                                        except Exception:
                                            ei_int = -1
                                        self.model.editing_edge_index = ei_int
                                        edges = getattr(self.model, 'edges', [])
                                        init = ''
                                        if isinstance(ei_int, int) and 0 <= ei_int < len(edges):
                                            e = edges[ei_int]
                                            init = str(e.get('label') or e.get('on') or e.get('event') or '')
                                        self.model.editing_text = init
                                        try:
                                            self.view.begin_text_edit(init, select_all=True)
                                        except Exception:
                                            pass
                                        return True
                                    return True
                            except Exception:
                                continue
                    except Exception:
                        pass
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                tool = getattr(self.model, 'active_graph_tool', 'select')
                if tool == 'select':
                    # Priority: if clicking on an edge handle, start edge-handle drag
                    handle_hit = closest_handle(local_x, local_y)
                    if handle_hit is not None:
                        ei, end = handle_hit
                        edges = getattr(self.model, 'edges', [])
                        if isinstance(ei, int) and 0 <= ei < len(edges):
                            self.model.selected_edge_index = ei
                            self.model.dragging_edge_index = ei
                            self.model.dragging_edge_end = end
                            self.model.dragging_edge_preview_x = float(wx)
                            self.model.dragging_edge_preview_y = float(wy)
                            self.model.dragging_edge_orig_from = edges[ei].get('from')
                            self.model.dragging_edge_orig_to = edges[ei].get('to')
                            LOGGER.debug("[GraphPanel][EDGE DRAG START] edge=%s end=%s world=(%.1f,%.1f)", ei, end, wx, wy)
                            return True
                    # Otherwise fall back to node select/drag
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
                        try:
                            self._persist_sets_structural()
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
                        try:
                            self._persist_sets_structural()
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
                        try:
                            self._persist_sets_structural()
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
                        try:
                            self._persist_sets_structural()
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
                        try:
                            self._persist_sets_structural()
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
                        try:
                            self._persist_sets_structural()
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
            if btn == 1 and self.model.dragging_node_id is not None and getattr(self.model, 'dragging_edge_index', None) is None:
                self.model.dragging_node_id = None
                # Persist layout after finishing a node drag
                try:
                    self._persist_layout()
                except Exception:
                    pass
                return True
            if btn == 1 and getattr(self.model, 'dragging_edge_index', None) is not None:
                # Finalize edge handle drag: snap to node if dropping over a node; otherwise cancel (revert)
                ei = int(getattr(self.model, 'dragging_edge_index', -1) or -1)
                end = getattr(self.model, 'dragging_edge_end', None)
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                changed = False
                edges = getattr(self.model, 'edges', [])
                if node is not None and isinstance(ei, int) and 0 <= ei < len(edges) and end in ('from', 'to'):
                    nid = node.get('id')
                    try:
                        if end == 'from':
                            edges[ei]['from'] = nid
                        else:
                            edges[ei]['to'] = nid
                        changed = True
                    except Exception:
                        changed = False
                LOGGER.debug("[GraphPanel][EDGE DRAG END] edge=%s end=%s world=(%.1f,%.1f) changed=%s", ei, end, wx, wy, changed)
                # Clear drag state
                self.model.dragging_edge_index = None
                self.model.dragging_edge_end = None
                self.model.dragging_edge_preview_x = None
                self.model.dragging_edge_preview_y = None
                self.model.dragging_edge_orig_from = None
                self.model.dragging_edge_orig_to = None
                self.model.hover_edge_handle_end = None
                if changed:
                    try:
                        self._persist_sets_structural()
                    except Exception:
                        pass
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
            # IMPORTANT: Do NOT start node-drag if an edge-handle drag is active.
            # This was causing mouse-up to release a node drag first and cancel the edge reconnection.
            if (
                left_down_now
                and getattr(self.model, 'dragging_node_id', None) is None
                and getattr(self.model, 'dragging_edge_index', None) is None
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

            # Edge handle drag move: update preview world coordinates
            if getattr(self.model, 'dragging_edge_index', None) is not None:
                wx, wy = to_world(local_x, local_y)
                self.model.dragging_edge_preview_x = float(wx)
                self.model.dragging_edge_preview_y = float(wy)
                return True

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
                    for ei, pts in (paths.items() if isinstance(paths, dict) else []):
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
                # Additionally compute whether a specific handle is hovered for the hovered edge
                hover_end = None
                if hover_e is not None:
                    try:
                        ends = (getattr(self.view, 'edge_endpoints_local', {}) or {}).get(int(hover_e))
                        if isinstance(ends, dict):
                            rad = 8
                            rad2 = rad * rad
                            for side in ('from', 'to'):
                                p = ends.get(side)
                                if not p:
                                    continue
                                dx = ex - int(p[0])
                                dy = ey - int(p[1])
                                if dx*dx + dy*dy <= rad2:
                                    hover_end = side
                                    break
                    except Exception:
                        hover_end = None
                self.model.hover_edge_handle_end = hover_end
            except Exception:
                self.model.hover_edge_index = None
                self.model.hover_edge_handle_end = None

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
        entry["viewport"] = {
            "zoom": zoom,
            "pan_x": pan_x,
            "pan_y": pan_y,
            "legend_collapsed": bool(getattr(self.model, 'legend_collapsed', False)),
        }
        by_set[set_id] = entry
        layouts["by_set"] = by_set
        save_layouts(layouts, path)

    def _persist_sets_structural(self) -> None:
        """Write current nodes/edges into the selected set within sets.json.
        - Adds/removes states based on nodes
        - Updates set.initial from node flags
        - Rebuilds transitions from edges, preserving existing 'when' if possible
        """
        set_id = getattr(self.model, 'selected_set_id', None)
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
        for n in getattr(self.model, 'nodes', []):
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
        # Initial selection: prefer explicitly marked node; otherwise keep previous if still present; else fallback to first
        new_ids = [st.get('id') for st in new_states if isinstance(st.get('id'), str)]
        if initial_node_id:
            target['initial'] = initial_node_id
        else:
            prev_init = target.get('initial')
            if prev_init not in new_ids:
                if new_ids:
                    target['initial'] = new_ids[0]
        # Transitions: build from edges, preserve existing 'when' if possible
        existing_trs = target.get('transitions') or []
        by_pair = {}
        for tr in existing_trs:
            key = (tr.get('from'), tr.get('to'))
            by_pair.setdefault(key, []).append(tr)
        new_trs = []
        for e in getattr(self.model, 'edges', []):
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
        # Save (validates and codegens via save_sets)
        save_sets(data, path)
        # Hot-reload runtime snapshot
        try:
            publish_reload()
        except Exception:
            pass


__all__ = ["FsmGraphPanelController"]
