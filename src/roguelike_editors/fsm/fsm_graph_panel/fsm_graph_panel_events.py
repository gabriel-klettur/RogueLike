from __future__ import annotations
import logging
import math


class FsmGraphPanelEventHandler:
    def handle_event(self, controller, event) -> bool:
        # Delegates key parts of event handling from the controller.
        # Initial scope: toolbar events, inline text editing, ESC cancel, toolbar clicks, legend toggle.
        try:
            import pygame  # type: ignore
        except Exception:
            return False

        model = getattr(controller, 'model', None)
        view = getattr(controller, 'view', None)
        if not getattr(model, 'visible', False):
            return False

        rect = getattr(view, 'canvas_rect', None)
        if rect is None:
            return False

        et = getattr(event, 'type', None)
        mouse_pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        btn = getattr(event, 'button', None)
        inside = rect.collidepoint(mouse_pos)
        local_x = mouse_pos[0] - rect.left
        local_y = mouse_pos[1] - rect.top
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
        zoom = float(getattr(model, 'zoom', 1.0))

        # Helpers
        def to_world(lx, ly):
            z = max(0.05, float(getattr(model, 'zoom', 1.0)))
            return ((lx - float(getattr(model, 'pan_x', 0.0))) / z, (ly - float(getattr(model, 'pan_y', 0.0))) / z)

        def pick_node(wx, wy):
            for n in reversed(list(getattr(model, 'nodes', []))):
                nx = int(n.get('x', 0)); ny = int(n.get('y', 0))
                nw = int(n.get('w', 120)); nh = int(n.get('h', 60))
                if nx <= wx <= nx + nw and ny <= wy <= ny + nh:
                    return n
            return None

        # Delegate mouse wheel and other toolbar-handled events to toolbar events
        try:
            if et == pygame.MOUSEWHEEL:
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][WHEEL] delegating to toolbar_events. mouse=%s rect=%s", mouse_pos, rect
                )
            if getattr(controller, 'toolbar_events', None) and controller.toolbar_events.handle_event(
                event, canvas_rect=rect, graph_model=model
            ):
                # Persist viewport (zoom/pan) after toolbar-handled zoom
                try:
                    controller._persist_layout()
                except Exception:
                    pass
                return True
        except Exception:
            pass

        # Inline text editing active: delegate events to TextInput and swallow others
        try:
            ti = getattr(view, 'text_input', None)
        except Exception:
            ti = None
        if ti is not None and getattr(ti, 'active', False):
            # Cancel on ESC
            if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
                try:
                    ti.deactivate()
                except Exception:
                    pass
                model.editing_node_id = None
                model.editing_edge_index = None
                model.editing_text = None
                return True
            # Delegate to widget (translate local mouse coords for hit-testing)
            try:
                if et == pygame.MOUSEBUTTONDOWN:
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
                    model.editing_text = str(getattr(ti, 'text', '') or '')
                except Exception:
                    pass
                # Commit when widget deactivates (Enter)
                if not getattr(ti, 'active', False):
                    text = str(getattr(ti, 'text', '') or '')
                    if getattr(model, 'editing_node_id', None):
                        nid = model.editing_node_id
                        for n in getattr(model, 'nodes', []):
                            if n.get('id') == nid:
                                n['label'] = text
                                break
                    elif getattr(model, 'editing_edge_index', None) is not None:
                        try:
                            ei = int(model.editing_edge_index)  # type: ignore[arg-type]
                        except Exception:
                            ei = -1
                        edges = getattr(model, 'edges', [])
                        if isinstance(ei, int) and 0 <= ei < len(edges):
                            edges[ei]['label'] = text
                    model.editing_node_id = None
                    model.editing_edge_index = None
                    model.editing_text = None
                    try:
                        controller._persist_sets_structural()
                    except Exception:
                        pass
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                return True
            # Click outside input rectangle: commit and close
            if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                abs_r = getattr(view, 'text_input_abs_rect', None)
                if abs_r is None or not abs_r.collidepoint(mouse_pos):
                    try:
                        ti.deactivate()
                    except Exception:
                        pass
                    text = str(getattr(ti, 'text', '') or '')
                    if getattr(model, 'editing_node_id', None):
                        nid = model.editing_node_id
                        for n in getattr(model, 'nodes', []):
                            if n.get('id') == nid:
                                n['label'] = text
                                break
                    elif getattr(model, 'editing_edge_index', None) is not None:
                        try:
                            ei = int(model.editing_edge_index)  # type: ignore[arg-type]
                        except Exception:
                            ei = -1
                        edges = getattr(model, 'edges', [])
                        if isinstance(ei, int) and 0 <= ei < len(edges):
                            edges[ei]['label'] = text
                    model.editing_node_id = None
                    model.editing_edge_index = None
                    model.editing_text = None
                    try:
                        controller._persist_sets_structural()
                    except Exception:
                        pass
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                    return True
            # Swallow other events while editing
            return True

        # Global ESC handling when not inline-editing: cancel drags or pending connect/disconnect
        try:
            if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
                # Cancel edge handle drag
                if getattr(model, 'dragging_edge_index', None) is not None or getattr(model, 'dragging_edge_id', None) is not None:
                    model.dragging_edge_index = None
                    model.dragging_edge_id = None
                    model.dragging_edge_end = None
                    model.dragging_edge_preview_x = None
                    model.dragging_edge_preview_y = None
                    model.dragging_edge_orig_from = None
                    model.dragging_edge_orig_to = None
                    model.hover_edge_handle_end = None
                    return True
                # Cancel pending connect/disconnect source selection
                tool = getattr(model, 'active_graph_tool', 'select')
                if tool in ('connect', 'disconnect') and getattr(model, 'connect_source_node_id', None):
                    model.connect_source_node_id = None
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                    return True
        except Exception:
            pass

        # Handle clicks on the graph toolbar buttons via toolbar controller
        if et == pygame.MOUSEBUTTONDOWN and btn == 1:
            try:
                if getattr(controller, 'toolbar', None) and controller.toolbar.handle_mouse_down(mouse_pos, rect, model):
                    # Persist viewport/tool state after toolbar interaction (e.g., zoom)
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                    # Activate tool runtime (if any) after changing active_graph_tool
                    try:
                        if hasattr(controller, '_activate_tool'):
                            controller._activate_tool(getattr(model, 'active_graph_tool', 'select'))
                    except Exception:
                        pass
                    return True
            except Exception:
                pass

        # Legend minimize/expand toggle and click capture
        if et == pygame.MOUSEBUTTONDOWN and btn == 1:
            try:
                lbr = getattr(view, 'legend_button_rect', None)
                lrect = getattr(view, 'legend_rect', None)
                # Click on button toggles
                if lbr is not None and lbr.collidepoint(mouse_pos):
                    model.legend_collapsed = not bool(getattr(model, 'legend_collapsed', False))
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                    return True
                # Click inside legend body: consume; expand if collapsed
                if lrect is not None and lrect.collidepoint(mouse_pos):
                    if bool(getattr(model, 'legend_collapsed', False)):
                        model.legend_collapsed = False
                        try:
                            controller._persist_layout()
                        except Exception:
                            pass
                    return True
            except Exception:
                pass

        # Active tool delegation (non-select): let the current tool handle events first
        try:
            tool_key = str(getattr(model, 'active_graph_tool', 'select') or 'select')
        except Exception:
            tool_key = 'select'
        if tool_key != 'select':
            try:
                if hasattr(controller, '_dispatch_active_tool_event') and controller._dispatch_active_tool_event(event):
                    return True
            except Exception:
                pass

        # Middle mouse pan start
        if et == pygame.MOUSEBUTTONDOWN and btn == 2:
            logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                "[GraphPanel][PAN START] inside=%s mouse=%s local=(%d,%d) pan=(%s,%s) zoom=%.3f",
                inside, mouse_pos, int(local_x), int(local_y),
                getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                float(getattr(model, 'zoom', 1.0)),
            )
            model.dragging_pan = True
            model.drag_last_local_x = int(local_x)
            model.drag_last_local_y = int(local_y)
            return True

        # Mouse button up handling: node drag end, edge drag finalize, pan end
        if et == pygame.MOUSEBUTTONUP:
            if btn == 1 and getattr(model, 'dragging_node_id', None) is not None and getattr(model, 'dragging_edge_index', None) is None:
                model.dragging_node_id = None
                try:
                    controller._persist_layout()
                except Exception:
                    pass
                return True
            if btn == 1 and (getattr(model, 'dragging_edge_index', None) is not None or getattr(model, 'dragging_edge_id', None) is not None):
                # Finalize edge handle drag: snap to node if dropping over a node; otherwise cancel (revert)
                try:
                    ei_val = getattr(model, 'dragging_edge_index', None)
                    ei = int(ei_val) if ei_val is not None else -1
                except Exception:
                    ei = -1
                eid = getattr(model, 'dragging_edge_id', None)
                end = getattr(model, 'dragging_edge_end', None)
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                changed = False
                edges = getattr(model, 'edges', [])
                # Resolve edge index via ID if available
                try:
                    if not isinstance(eid, str) or not eid:
                        if len(getattr(model, 'edge_id_by_index', []) or []) != len(edges or []):
                            model.rebuild_caches()
                        if isinstance(ei, int) and 0 <= ei < len(getattr(model, 'edge_id_by_index', []) or []):
                            eid = model.edge_id_by_index[ei]
                        else:
                            eid = None
                    if isinstance(eid, str):
                        if len(getattr(model, 'edge_index_by_id', {}) or {}) != len(getattr(model, 'edge_id_by_index', []) or []):
                            model.rebuild_caches()
                        ei_now = getattr(model, 'edge_index_by_id', {}).get(eid)
                    else:
                        ei_now = ei if isinstance(ei, int) else None
                except Exception:
                    ei_now = ei if isinstance(ei, int) else None
                if node is not None and isinstance(ei_now, int) and 0 <= ei_now < len(edges) and end in ('from', 'to'):
                    nid = node.get('id')
                    try:
                        if end == 'from':
                            edges[ei_now]['from'] = nid
                        else:
                            edges[ei_now]['to'] = nid
                        changed = True
                    except Exception:
                        changed = False
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][EDGE DRAG END] edge_idx=%s edge_id=%s end=%s world=(%.1f,%.1f) changed=%s",
                    ei, eid, end, wx, wy, changed,
                )
                # Clear drag state
                model.dragging_edge_index = None
                model.dragging_edge_id = None
                model.dragging_edge_end = None
                model.dragging_edge_preview_x = None
                model.dragging_edge_preview_y = None
                model.dragging_edge_orig_from = None
                model.dragging_edge_orig_to = None
                model.hover_edge_handle_end = None
                if changed:
                    try:
                        model.rebuild_caches()
                    except Exception:
                        pass
                    try:
                        controller._persist_sets_structural()
                    except Exception:
                        pass
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                return True
            if btn == 2 and getattr(model, 'dragging_pan', False):
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][PAN END] mouse=%s local=(%d,%d) pan=(%s,%s)",
                    mouse_pos, int(local_x), int(local_y),
                    getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                )
                model.dragging_pan = False
                try:
                    controller._persist_layout()
                except Exception:
                    pass
                return True

        # Mouse motion: start pan with mid, start node drag with left, move node/pan/edge preview, update hovers
        if et == pygame.MOUSEMOTION:
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

            if mid_down_now and not getattr(model, 'dragging_pan', False) and inside:
                if getattr(model, 'dragging_node_id', None) is not None:
                    model.dragging_node_id = None
                model.dragging_pan = True
                model.drag_last_local_x = int(local_x)
                model.drag_last_local_y = int(local_y)
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][PAN START@MOTION] mouse=%s local=(%d,%d) pan=(%s,%s) zoom=%.3f",
                    mouse_pos, int(local_x), int(local_y),
                    getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                    float(getattr(model, 'zoom', 1.0)),
                )
                return True

            if (
                left_down_now
                and getattr(model, 'dragging_node_id', None) is None
                and getattr(model, 'dragging_edge_index', None) is None
                and getattr(model, 'dragging_edge_id', None) is None
                and not getattr(model, 'dragging_pan', False)
                and getattr(model, 'active_graph_tool', 'select') == 'select'
                and inside
            ):
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                if node is not None:
                    model.selected_node_id = node.get('id')
                    model.dragging_node_id = node.get('id')
                    model.drag_offset_x = node.get('x', 0) - wx
                    model.drag_offset_y = node.get('y', 0) - wy

            # Edge handle drag move: update preview world coordinates
            if getattr(model, 'dragging_edge_index', None) is not None or getattr(model, 'dragging_edge_id', None) is not None:
                wx, wy = to_world(local_x, local_y)
                model.dragging_edge_preview_x = float(wx)
                model.dragging_edge_preview_y = float(wy)
                return True

            if getattr(model, 'dragging_node_id', None) and getattr(model, 'active_graph_tool', 'select') == 'select':
                wx, wy = to_world(local_x, local_y)
                nid = model.dragging_node_id
                for n in getattr(model, 'nodes', []):
                    if n.get('id') == nid:
                        n['x'] = int(wx + model.drag_offset_x)
                        n['y'] = int(wy + model.drag_offset_y)
                        break
                return True
            if getattr(model, 'dragging_pan', False):
                dx = int(local_x) - int(getattr(model, 'drag_last_local_x', int(local_x)))
                dy = int(local_y) - int(getattr(model, 'drag_last_local_y', int(local_y)))
                before = (getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0))
                model.pan_x = pan_x + dx
                model.pan_y = pan_y + dy
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][PAN MOVE] mouse=%s local=(%d,%d) dx=%d dy=%d pan %s -> (%s,%s)",
                    mouse_pos, int(local_x), int(local_y), dx, dy, before,
                    getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                )
                model.drag_last_local_x = int(local_x)
                model.drag_last_local_y = int(local_y)
                return True

            # Hover tracking
            try:
                wx, wy = to_world(local_x, local_y)
                node = pick_node(wx, wy)
                model.hover_node_id = node.get('id') if node is not None else None
            except Exception:
                model.hover_node_id = None

            try:
                ex, ey = int(local_x), int(local_y)
                hover_e = None
                label_rects = getattr(view, 'edge_label_rects', {}) or {}
                for ei, r in (label_rects.items() if isinstance(label_rects, dict) else []):
                    try:
                        if r.collidepoint(ex, ey):
                            hover_e = ei
                            break
                    except Exception:
                        continue
                if hover_e is None:
                    paths = getattr(view, 'edge_paths', {}) or {}
                    best_e = None
                    best_d = 1e9
                    def _dist_pt_seg(px, py, ax, ay, bx, by):
                        vx, vy = bx - ax, by - ay
                        wx0, wy0 = px - ax, py - ay
                        vv = vx*vx + vy*vy
                        if vv <= 1e-6:
                            dx, dy = px - ax, py - ay
                            return math.hypot(dx, dy)
                        t = max(0.0, min(1.0, (wx0*vx + wy0*vy) / vv))
                        cx, cy = ax + t*vx, ay + t*vy
                        return math.hypot(px - cx, py - cy)
                    for ei, pts in (paths.items() if isinstance(paths, dict) else []):
                        try:
                            if not pts or len(pts) < 2:
                                continue
                            for i in range(len(pts)-1):
                                ax, ay = pts[i]
                                bx, by = pts[i+1]
                                d = _dist_pt_seg(ex, ey, ax, ay, bx, by)
                                if d < best_d:
                                    best_d = d
                                    best_e = ei
                        except Exception:
                            continue
                    tol = 8
                    hover_e = best_e if best_d <= tol else None
                model.hover_edge_index = hover_e
                try:
                    if hover_e is not None:
                        if len(getattr(model, 'edge_id_by_index', []) or []) != len(getattr(model, 'edges', []) or []):
                            model.rebuild_caches()
                        idx = int(hover_e)
                        if 0 <= idx < len(model.edge_id_by_index):
                            model.hover_edge_id = model.edge_id_by_index[idx]
                        else:
                            model.hover_edge_id = None
                    else:
                        model.hover_edge_id = None
                except Exception:
                    model.hover_edge_id = None
                hover_end = None
                if hover_e is not None:
                    try:
                        ends = (getattr(view, 'edge_endpoints_local', {}) or {}).get(int(hover_e))
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
                model.hover_edge_handle_end = hover_end
            except Exception:
                model.hover_edge_index = None
                model.hover_edge_handle_end = None

            return True

        # Fallback: some environments emit wheel as buttons 4/5
        if et == pygame.MOUSEBUTTONDOWN and btn in (4, 5):
            y = 1 if btn == 4 else -1
            factor = 1.1 ** y
            logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                "[GraphPanel][WHEEL BTN] handler handling btn=%s y=%s mouse=%s factor=%.3f", btn, y, mouse_pos, factor
            )
            if factor != 1.0:
                old_z = max(0.05, zoom)
                new_z = max(0.2, min(3.0, old_z * factor))
                if abs(new_z - old_z) > 1e-6:
                    wx, wy = to_world(local_x, local_y)
                    model.zoom = new_z
                    model.pan_x = local_x - wx * new_z
                    model.pan_y = local_y - wy * new_z
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                return True

        if et == pygame.MOUSEWHEEL:
            y = getattr(event, 'y', 0)
            factor = 1.1 ** y
            logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                "[GraphPanel][WHEEL] handler handling y=%s mouse=%s factor=%.3f", y, mouse_pos, factor
            )
            if factor != 1.0:
                old_z = max(0.05, zoom)
                new_z = max(0.2, min(3.0, old_z * factor))
                if abs(new_z - old_z) > 1e-6:
                    wx, wy = to_world(local_x, local_y)
                    model.zoom = new_z
                    model.pan_x = local_x - wx * new_z
                    model.pan_y = local_y - wy * new_z
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                return True

        return False


__all__ = ["FsmGraphPanelEventHandler"]
