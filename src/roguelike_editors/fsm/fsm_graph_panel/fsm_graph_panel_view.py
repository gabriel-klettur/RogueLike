from __future__ import annotations
from roguelike_ui.widgets.text_input import TextInput


class FsmGraphPanelView:
    def __init__(self) -> None:
        self.canvas_rect = None
        # Last rendered label rects (in local canvas coordinates)
        self.node_label_rects = {}
        self.edge_label_rects = {}
        # Last rendered edge paths (list of local points) for hover proximity checks
        self.edge_paths = {}
        # Last rendered edge endpoints in local (canvas) coordinates: {edge_idx: {"from": (x,y), "to": (x,y)}}
        self.edge_endpoints_local = {}
        # Legend overlay rects (screen-space)
        self.legend_rect = None
        self.legend_button_rect = None
        # Inline text input widget and absolute rect for outside-click checks
        self.text_input: TextInput | None = None
        self.text_input_abs_rect = None
        self._pending_text_edit: tuple[str, bool] | None = None

    # Called by controller when user starts an edit (double-click on label)
    def begin_text_edit(self, initial_text: str, select_all: bool = False) -> None:
        # Defer actual creation/activation to render; store initial text
        self._pending_text_edit = (str(initial_text or ''), bool(select_all))

    def render(self, model, screen, *, anchor=(360, 120), toolbar=None):
        if not getattr(model, "visible", True):
            return None
        try:
            import pygame  # type: ignore
        except Exception:
            return None
        # Canvas placement and size (temporary fixed size)
        x, y = anchor
        w, h = 800, 520
        self.canvas_rect = pygame.Rect(x, y, w, h)

        # Background panel
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        # Use fully opaque background to completely hide any underlying game elements
        # when the FSM Graph Panel is visible.
        surf.fill((15, 15, 18, 255))
        # Reset last label rects for new frame
        self.node_label_rects = {}
        self.edge_label_rects = {}
        self.edge_paths = {}
        self.edge_endpoints_local = {}
        # Reset legend rects
        self.legend_rect = None
        self.legend_button_rect = None

        # Draw top graph toolbar (horizontal) via toolbar submodule
        tb_h = 0
        if toolbar is not None:
            try:
                active_tool = getattr(model, 'active_graph_tool', None)
                # Expect toolbar to be a controller with .view and .model
                tb_h = int(toolbar.view.render_into(surf, toolbar.model, screen_origin=(x, y), width=w, active_tool=active_tool) or 0)
            except Exception:
                tb_h = 0
        # Pan/zoom parameters
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
        zoom = max(0.05, float(getattr(model, 'zoom', 1.0)))

        # Helper to transform world->local in-canvas
        def W(p):
            return (int(p[0] * zoom + pan_x), int(p[1] * zoom + pan_y))

        # Grid that respects pan/zoom (infinite-style)
        try:
            base_grid = 40
            grid = max(8, int(base_grid * zoom))
            grid_color = (30, 30, 34)
            # offset so grid scrolls smoothly
            ox = int(pan_x) % grid
            oy = int(pan_y) % grid
            # Avoid overdrawing under the toolbar visually by starting after tb_h
            tb_h = int(tb_h)
            # Vertical grid lines: only draw below toolbar
            for gx in range(-ox, w, grid):
                pygame.draw.line(surf, grid_color, (gx, tb_h), (gx, h), 1)
            # Horizontal grid lines: align with pan offset and start at first y >= tb_h
            start_y = tb_h + ((-oy - tb_h) % grid)
            for gy in range(start_y, h, grid):
                pygame.draw.line(surf, grid_color, (0, gy), (w, gy), 1)
        except Exception:
            pass

        

        

        # Edges (draw beneath nodes) as arrows with edge-to-edge anchors, curves, and labels
        try:
            import math
            node_pos = {n['id']: (int(n.get('x', 0)), int(n.get('y', 0)), int(n.get('w', 120)), int(n.get('h', 60))) for n in getattr(model, 'nodes', [])}

            def _dominant_side(dx, dy):
                if abs(dx) >= abs(dy):
                    return 'right' if dx >= 0 else 'left'
                else:
                    return 'bottom' if dy >= 0 else 'top'

            def _edge_point_with_slot(rect, toward, slot_idx, slot_total):
                # Distribute attachment along the chosen side using slots
                x, y, w, h = rect
                cx, cy = x + w/2.0, y + h/2.0
                dx, dy = toward[0] - cx, toward[1] - cy
                # choose side by dominant direction
                side = _dominant_side(dx, dy)
                # t in (0,1) avoiding corners
                denom = max(1, int(slot_total) + 1)
                t = (int(slot_idx) + 1) / float(denom)
                pad = 0.05  # keep away from corners
                t = pad + (1 - 2*pad) * t
                if side == 'left':
                    return (x, y + h * t)
                if side == 'right':
                    return (x + w, y + h * t)
                if side == 'top':
                    return (x + w * t, y)
                # bottom
                return (x + w * t, y + h)

            def _quad_point(p0, p1, p2, t):
                it = 1.0 - t
                return (
                    it*it*p0[0] + 2*it*t*p1[0] + t*t*p2[0],
                    it*it*p0[1] + 2*it*t*p1[1] + t*t*p2[1],
                )

            def _quad_tangent(p0, p1, p2, t):
                # derivative of quadratic Bezier
                dx = 2*(1-t)*(p1[0]-p0[0]) + 2*t*(p2[0]-p1[0])
                dy = 2*(1-t)*(p1[1]-p0[1]) + 2*t*(p2[1]-p1[1])
                return (dx, dy)

            def _draw_polyline(surface, color, points, width=2):
                if len(points) >= 2:
                    pygame.draw.lines(surface, color, False, points, width)

            def _arrowhead(surface, color, tip, direction, *, head_len=14, head_width=10):
                vx, vy = direction
                mag = math.hypot(vx, vy)
                if mag < 1e-3:
                    return
                ux, uy = vx/mag, vy/mag
                bx = tip[0] - ux * head_len
                by = tip[1] - uy * head_len
                px, py = -uy, ux
                hw = head_width / 2.0
                p_left = (bx + px * hw, by + py * hw)
                p_right = (bx - px * hw, by - py * hw)
                pygame.draw.polygon(surface, color, [p_left, p_right, tip])

            # Prepare grouping to offset parallel edges and handle self-loops + per-side ports
            edges = list(getattr(model, 'edges', []))
            pair_counts = {}
            dir_counts = {}
            # Group edges per node side for port distribution
            src_groups = {}  # key: (node_id, side) -> [edge_index]
            dst_groups = {}
            for e in edges:
                fr = e.get('from'); to = e.get('to')
                if fr is None or to is None: 
                    continue
                if fr == to:
                    key = ('self', fr)
                else:
                    key = tuple(sorted([fr, to]))
                pair_counts[key] = pair_counts.get(key, 0) + 1
                dkey = (fr, to)
                dir_counts[dkey] = dir_counts.get(dkey, 0) + 1

            # Pre-pass to register per-side groups
            for idx, e in enumerate(edges):
                fr = e.get('from'); to = e.get('to')
                if fr not in node_pos or to not in node_pos:
                    continue
                edge_i = idx  # preserve enumerate index for mapping/hover
                sx, sy, sw, sh = node_pos[fr]
                tx, ty, tw, th = node_pos[to]
                sc = (sx + sw/2.0, sy + sh/2.0)
                tc = (tx + tw/2.0, ty + th/2.0)
                sdx, sdy = (tc[0]-sc[0], tc[1]-sc[1])
                ddx, ddy = (sc[0]-tc[0], sc[1]-tc[1])
                s_side = _dominant_side(sdx, sdy)
                d_side = _dominant_side(ddx, ddy)
                src_groups.setdefault((fr, s_side), []).append(idx)
                dst_groups.setdefault((to, d_side), []).append(idx)

            # Running index per directed pair to alternate offsets
            dir_index = {}

            for idx, e in enumerate(edges):
                fr = e.get('from'); to = e.get('to')
                if fr not in node_pos or to not in node_pos:
                    continue
                edge_i = idx  # ensure consistent index for hover mapping
                sx, sy, sw, sh = node_pos[fr]
                tx, ty, tw, th = node_pos[to]
                sc = (sx + sw/2.0, sy + sh/2.0)
                tc = (tx + tw/2.0, ty + th/2.0)
                # Resolve hover/selection state using edge ID or index
                eid = e.get('id')
                hover_id = getattr(model, 'hover_edge_id', None)
                is_edge_hover = (idx == getattr(model, 'hover_edge_index', None)) or (isinstance(eid, str) and eid == hover_id)
                sel_id = getattr(model, 'selected_edge_id', None)
                is_edge_selected = (idx == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and eid == sel_id)
                color = e.get('color', (120, 120, 140))
                if e.get('active'):
                    color = (255, 210, 90)
                elif is_edge_selected:
                    color = (255, 220, 110)
                elif is_edge_hover:
                    color = (255, 230, 120)
                width = int(e.get('width', 2))
                if is_edge_selected:
                    width = max(width + 2, 4)
                elif is_edge_hover:
                    width = max(width + 1, 3)
                head_len = int(e.get('head_len', 14))
                head_width = int(e.get('head_width', 10))

                # Determine if curve is needed
                if fr == to:
                    # Self-loop: control point above the node, anchor at top-center
                    loop_h = max(sh, 60)
                    p0 = (sx + sw/2.0, sy)
                    p2 = p0
                    ctrl = (sc[0] + sw*0.8, sy - loop_h)  # loop to the top-right
                    # Sample curve
                    samples = 18
                    pts = [_quad_point(p0, ctrl, p2, t/float(samples)) for t in range(samples+1)]
                    pts = [W(p) for p in pts]
                    # Shorten for arrowhead base
                    if len(pts) >= 2:
                        p_tip = pts[-1]
                        p_prev = pts[-2]
                        dir_vec = (p_tip[0]-p_prev[0], p_tip[1]-p_prev[1])
                        _draw_polyline(surf, color, pts[:-1], width)
                        _arrowhead(surf, color, p_tip, dir_vec, head_len=head_len, head_width=head_width)
                    # Store endpoints (both ends coincide for loops)
                    try:
                        lp = W(p0)
                        self.edge_endpoints_local[edge_i] = {"from": lp, "to": lp}
                        if isinstance(eid, str):
                            self.edge_endpoints_local[eid] = {"from": lp, "to": lp}
                    except Exception:
                        pass
                    # Store path for hover proximity
                    try:
                        self.edge_paths[edge_i] = list(pts)
                        if isinstance(eid, str):
                            self.edge_paths[eid] = list(pts)
                    except Exception:
                        pass
                    label = e.get('label') or e.get('on') or e.get('event')
                    is_editing = (getattr(model, 'editing_edge_index', None) == edge_i) or (isinstance(eid, str) and getattr(model, 'editing_edge_id', None) == eid)
                    if label or is_editing:
                        is_hover = (edge_i == getattr(model, 'hover_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'hover_edge_id', None) == eid)
                        is_selected = (edge_i == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'selected_edge_id', None) == eid)
                        is_focus = is_hover or is_selected
                        font = pygame.font.SysFont(None, 20 if is_focus else 18)
                        mid = _quad_point(p0, ctrl, p2, 0.35)
                        mid = W(mid)
                        # Compute rect (even when editing to place TextInput), but don't blit label during edit
                        text_for_rect = str(getattr(model, 'editing_text', '') or '') if is_editing else str(label or '')
                        txt = font.render(text_for_rect, True, (255,230,120) if is_focus else (210,210,210))
                        tr = txt.get_rect(center=(mid[0], mid[1]))
                        if not is_editing:
                            surf.blit(txt, tr)
                        try:
                            self.edge_label_rects[edge_i] = tr.copy()
                            if isinstance(eid, str):
                                self.edge_label_rects[eid] = tr.copy()
                        except Exception:
                            pass
                    continue

                pair_key = tuple(sorted([fr, to]))
                dkey = (fr, to)
                dir_i = dir_index.get(dkey, 0)
                dir_index[dkey] = dir_i + 1

                # Base straight path start/end: from edge of rectangles with per-side ports
                # Find slot indices for source and dest
                sdx, sdy = (tc[0]-sc[0], tc[1]-sc[1])
                ddx, ddy = (sc[0]-tc[0], sc[1]-tc[1])
                s_side = _dominant_side(sdx, sdy)
                d_side = _dominant_side(ddx, ddy)
                s_list = src_groups.get((fr, s_side), [idx])
                d_list = dst_groups.get((to, d_side), [idx])
                s_idx = s_list.index(idx) if idx in s_list else 0
                d_idx = d_list.index(idx) if idx in d_list else 0
                p_start = _edge_point_with_slot((sx, sy, sw, sh), tc, s_idx, len(s_list))
                p_end = _edge_point_with_slot((tx, ty, tw, th), sc, d_idx, len(d_list))

                # Perpendicular offset for parallel edges and two-way edges
                total_in_pair = pair_counts.get(pair_key, 1)
                need_curve = total_in_pair > 1 or e.get('curved')
                if need_curve:
                    # Perpendicular unit vector
                    dx, dy = (p_end[0]-p_start[0], p_end[1]-p_start[1])
                    dlen = math.hypot(dx, dy) or 1.0
                    nx, ny = -dy/dlen, dx/dlen
                    step = float(e.get('curve_step', 24))
                    # Adaptive curvature boost for axis-aligned edges
                    ux, uy = dx/dlen, dy/dlen
                    alignment = max(abs(ux), abs(uy))  # 1 for axis-aligned
                    step *= (1.0 + 0.75 * alignment)
                    # Alternate sides and increase magnitude: 0, +1, -1, +2, -2 ...
                    sign = 1 if (dir_i % 2 == 0) else -1
                    mult = (dir_i // 2) + 1
                    offset = sign * mult * step
                    mid = ((p_start[0]+p_end[0])/2.0, (p_start[1]+p_end[1])/2.0)
                    ctrl = (mid[0] + nx*offset, mid[1] + ny*offset)
                    # Build curve samples
                    samples = 18
                    pts = [_quad_point(p_start, ctrl, p_end, t/float(samples)) for t in range(samples+1)]
                    pts = [W(p) for p in pts]
                    if len(pts) >= 2:
                        p_tip = pts[-1]
                        p_prev = pts[-2]
                        dir_vec = (p_tip[0]-p_prev[0], p_tip[1]-p_prev[1])
                        _draw_polyline(surf, color, pts[:-1], width)
                        _arrowhead(surf, color, p_tip, dir_vec, head_len=head_len, head_width=head_width)
                    # Store endpoints for handle hover (local)
                    try:
                        self.edge_endpoints_local[idx] = {"from": W(p_start), "to": W(p_end)}
                        if isinstance(eid, str):
                            self.edge_endpoints_local[eid] = {"from": W(p_start), "to": W(p_end)}
                    except Exception:
                        pass
                    # Store path for hover proximity
                    try:
                        self.edge_paths[idx] = list(pts)
                        if isinstance(eid, str):
                            self.edge_paths[eid] = list(pts)
                    except Exception:
                        pass
                    label = e.get('label') or e.get('on') or e.get('event')
                    is_editing = (getattr(model, 'editing_edge_index', None) == idx) or (isinstance(eid, str) and getattr(model, 'editing_edge_id', None) == eid)
                    if label or is_editing:
                        is_hover = (idx == getattr(model, 'hover_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'hover_edge_id', None) == eid)
                        is_selected = (idx == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'selected_edge_id', None) == eid)
                        is_focus = is_hover or is_selected
                        font = pygame.font.SysFont(None, 20 if is_focus else 18)
                        mid_lbl = _quad_point(p_start, ctrl, p_end, 0.5)
                        mid_lbl = W(mid_lbl)
                        text_for_rect = str(getattr(model, 'editing_text', '') or '') if is_editing else str(label or '')
                        txt = font.render(text_for_rect, True, (255,230,120) if is_focus else (210,210,210))
                        tr = txt.get_rect(center=(mid_lbl[0], mid_lbl[1]))
                        if not is_editing:
                            surf.blit(txt, tr)
                        try:
                            self.edge_label_rects[idx] = tr.copy()
                            if isinstance(eid, str):
                                self.edge_label_rects[eid] = tr.copy()
                        except Exception:
                            pass
                else:
                    # Straight arrow with edge-anchored endpoints
                    dx, dy = (p_end[0]-p_start[0], p_end[1]-p_start[1])
                    p_start_l = W(p_start)
                    p_end_l = W(p_end)
                    _draw_polyline(surf, color, [p_start_l, (p_end_l[0]-dx*0.0001*zoom, p_end_l[1]-dy*0.0001*zoom)], width)
                    _arrowhead(surf, color, p_end_l, (dx*zoom, dy*zoom), head_len=head_len, head_width=head_width)
                    # Store path for hover proximity (simple 2-point polyline)
                    try:
                        self.edge_paths[idx] = [p_start_l, p_end_l]
                        if isinstance(eid, str):
                            self.edge_paths[eid] = [p_start_l, p_end_l]
                    except Exception:
                        pass
                    # Store endpoints for handle hover (local)
                    try:
                        self.edge_endpoints_local[idx] = {"from": p_start_l, "to": p_end_l}
                        if isinstance(eid, str):
                            self.edge_endpoints_local[eid] = {"from": p_start_l, "to": p_end_l}
                    except Exception:
                        pass
                    label = e.get('label') or e.get('on') or e.get('event')
                    is_editing = (getattr(model, 'editing_edge_index', None) == idx) or (isinstance(eid, str) and getattr(model, 'editing_edge_id', None) == eid)
                    if label or is_editing:
                        is_hover = (idx == getattr(model, 'hover_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'hover_edge_id', None) == eid)
                        is_selected = (idx == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'selected_edge_id', None) == eid)
                        is_focus = is_hover or is_selected
                        font = pygame.font.SysFont(None, 20 if is_focus else 18)
                        mid_lbl = ((p_start[0]+p_end[0])/2.0, (p_start[1]+p_end[1])/2.0)
                        mid_lbl = W(mid_lbl)
                        text_for_rect = str(getattr(model, 'editing_text', '') or '') if is_editing else str(label or '')
                        txt = font.render(text_for_rect, True, (255,230,120) if is_focus else (210,210,210))
                        tr = txt.get_rect(center=(mid_lbl[0], mid_lbl[1]))
                        if not is_editing:
                            surf.blit(txt, tr)
                        try:
                            self.edge_label_rects[idx] = tr.copy()
                            if isinstance(eid, str):
                                self.edge_label_rects[eid] = tr.copy()
                        except Exception:
                            pass
            # Re-draw hovered edge above other edges for better highlight visibility
            try:
                hover_eid = getattr(model, 'hover_edge_id', None)
                hover_ei = getattr(model, 'hover_edge_index', None)
                # Resolve key to fetch cached path (prefer ID)
                key = None
                if isinstance(hover_eid, str) and hover_eid in self.edge_paths:
                    key = hover_eid
                else:
                    try:
                        if hover_ei is not None and int(hover_ei) in self.edge_paths:
                            key = int(hover_ei)
                    except Exception:
                        key = None
                if key is not None:
                    pts = self.edge_paths.get(key)
                    if isinstance(pts, list) and len(pts) >= 2:
                        # Find the edge dict and compute style
                        idx = None
                        if isinstance(key, int):
                            idx = key
                        elif isinstance(key, str):
                            try:
                                idx = (getattr(model, 'edge_index_by_id', {}) or {}).get(key)
                            except Exception:
                                idx = None
                        e = None
                        edges = getattr(model, 'edges', [])
                        if isinstance(idx, int) and 0 <= idx < len(edges):
                            e = edges[idx]
                        elif isinstance(key, str):
                            # Fallback: linear scan by ID
                            try:
                                for i, ee in enumerate(edges):
                                    if ee.get('id') == key:
                                        idx = i
                                        e = ee
                                        break
                            except Exception:
                                e = None
                        if isinstance(e, dict):
                            eid = e.get('id')
                            # Selection/hover state
                            is_edge_selected = (idx == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and eid == getattr(model, 'selected_edge_id', None))
                            # Color/width similar to main pass, but ensure hover emphasis
                            color = e.get('color', (120, 120, 140))
                            if e.get('active'):
                                color = (255, 210, 90)
                            elif is_edge_selected:
                                color = (255, 220, 110)
                            else:
                                color = (255, 230, 120)
                            width = int(e.get('width', 2))
                            if is_edge_selected:
                                width = max(width + 2, 4)
                            else:
                                width = max(width + 1, 3)
                            head_len = int(e.get('head_len', 14))
                            head_width = int(e.get('head_width', 10))
                            # Draw polyline (excluding exact tip) and arrowhead using last segment direction
                            p_tip = pts[-1]
                            p_prev = pts[-2]
                            if len(pts) >= 3:
                                # Curved or multi-sampled path: draw all but the last sample
                                _draw_polyline(surf, color, pts[:-1], width)
                            else:
                                # Straight 2-point path: draw line to slightly before the tip
                                vx, vy = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
                                mag = math.hypot(vx, vy) or 1.0
                                # retract a tiny amount to avoid overdraw under the arrowhead
                                retract = 0.001 * mag
                                ux, uy = vx / mag, vy / mag
                                shortened_tip = (p_tip[0] - ux * retract, p_tip[1] - uy * retract)
                                _draw_polyline(surf, color, [pts[0], shortened_tip], width)
                            dir_vec = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
                            _arrowhead(surf, color, p_tip, dir_vec, head_len=head_len, head_width=head_width)
                            # Re-draw label above others (skip while editing)
                            is_editing = (getattr(model, 'editing_edge_index', None) == idx) or (isinstance(eid, str) and getattr(model, 'editing_edge_id', None) == eid)
                            if not is_editing:
                                label = e.get('label') or e.get('on') or e.get('event')
                                if label:
                                    try:
                                        # Prefer stored label rect center
                                        lr = self.edge_label_rects.get(key)
                                        if lr is None and isinstance(idx, int):
                                            lr = self.edge_label_rects.get(idx)
                                    except Exception:
                                        lr = None
                                    if lr is not None:
                                        font = pygame.font.SysFont(None, 20 if is_edge_selected else 20)
                                        txt = font.render(str(label), True, (255, 230, 120))
                                        tr = txt.get_rect(center=(lr.centerx, lr.centery))
                                        surf.blit(txt, tr)
            except Exception:
                pass
        except Exception:
            pass

        # Nodes
        try:
            base_font_size = 20
            base_color = (235, 235, 235)
            font = pygame.font.SysFont(None, base_font_size)
            for n in getattr(model, 'nodes', []):
                nx = int(n.get('x', 0)); ny = int(n.get('y', 0))
                nw = int(n.get('w', 120)); nh = int(n.get('h', 60))
                tl = W((nx, ny))
                rect = pygame.Rect(tl[0], tl[1], int(nw*zoom), int(nh*zoom))
                # body
                pygame.draw.rect(surf, (40, 44, 52), rect, 0, border_radius=6)
                # border (selected/hover/special/terminal/initial highlighted)
                is_hover_node = (n.get('id') == getattr(model, 'hover_node_id', None))
                if n.get('id') == getattr(model, 'selected_node_id', None):
                    color = (255, 210, 90)
                    border_w = 3
                elif is_hover_node:
                    color = (255, 230, 120)
                    border_w = 3
                else:
                    # Special states styling (damage/alert/interrupt/external)
                    spec = n.get('special')
                    spec_l = spec.lower() if isinstance(spec, str) else None
                    nid = n.get('id')
                    ncls = n.get('class')
                    is_damage = (spec_l == 'damage') or (nid == 'Damage') or (ncls == 'DamageState')
                    is_alert = (spec_l == 'alert') or (nid == 'AlertChase') or (ncls == 'AlertChaseState')
                    is_interrupt = (spec_l == 'interrupt') or (spec_l == 'external') or (n.get('external_entry') is True)
                    if is_damage:
                        # Damage: purple highlight
                        color = (160, 80, 200)
                        border_w = 3
                    elif is_alert:
                        # Alert-chase or alert-like: magenta/pink highlight
                        color = (220, 100, 180)
                        border_w = 3
                    elif is_interrupt:
                        # External-entry/interruptible: cyan highlight
                        color = (90, 200, 220)
                        border_w = 3
                    elif n.get('terminal'):
                        # Terminal/end nodes: red highlight
                        color = (220, 80, 80)
                        border_w = 3
                    elif n.get('initial'):
                        # Initial/start nodes: green highlight
                        color = (80, 200, 120)
                        border_w = 3
                    else:
                        color = (90, 90, 100)
                        border_w = 2
                pygame.draw.rect(surf, color, rect, border_w, border_radius=6)
                # label (hover highlight)
                is_hover = (n.get('id') == getattr(model, 'hover_node_id', None))
                label = str(n.get('label', n.get('id', '?')))
                node_font = pygame.font.SysFont(None, (base_font_size + 2) if is_hover else base_font_size)
                editing_this = (getattr(model, 'editing_node_id', None) == n.get('id'))
                text_for_rect = str(getattr(model, 'editing_text', label) if editing_this else label)
                txt = node_font.render(text_for_rect, True, (255, 230, 120) if is_hover else base_color)
                tr = txt.get_rect(center=(rect.centerx, rect.centery))
                # Do not draw the node label if currently editing this node; still record rect
                if not editing_this:
                    surf.blit(txt, tr)
                try:
                    self.node_label_rects[n.get('id')] = tr.copy()
                except Exception:
                    pass
        except Exception:
            pass
        
        # Overlay: edge handle circles and drag preview (drawn above nodes on the same canvas surface)
        try:
            import math
            # Draw handle circles for hovered edge or currently dragging edge
            hovered_e_idx = getattr(model, 'hover_edge_index', None)
            hovered_e_id = getattr(model, 'hover_edge_id', None)
            dragging_e_idx = getattr(model, 'dragging_edge_index', None)
            dragging_e_id = getattr(model, 'dragging_edge_id', None)
            hovered_end = getattr(model, 'hover_edge_handle_end', None)
            # Helper to draw a circle outline or filled for handle
            def _draw_handle(center, filled=False, radius=6):
                cx, cy = int(center[0]), int(center[1])
                color = (255, 230, 120)
                if filled:
                    pygame.draw.circle(surf, color, (cx, cy), radius)
                    pygame.draw.circle(surf, (40, 40, 44), (cx, cy), radius-2)
                    pygame.draw.circle(surf, color, (cx, cy), radius, 2)
                else:
                    pygame.draw.circle(surf, color, (cx, cy), radius, 2)
            # Draw for hovered edge
            ends = None
            if isinstance(hovered_e_id, str):
                ends = self.edge_endpoints_local.get(hovered_e_id)
            if ends is None and hovered_e_idx is not None:
                try:
                    ends = self.edge_endpoints_local.get(int(hovered_e_idx))
                except Exception:
                    ends = None
            if isinstance(ends, dict):
                fr = ends.get('from'); to = ends.get('to')
                if fr:
                    _draw_handle(fr, filled=(hovered_end == 'from'))
                if to:
                    _draw_handle(to, filled=(hovered_end == 'to'))
            # Draw for dragging edge (always show both handles on that edge)
            ends = None
            if isinstance(dragging_e_id, str):
                ends = self.edge_endpoints_local.get(dragging_e_id)
            if ends is None and dragging_e_idx is not None:
                try:
                    ends = self.edge_endpoints_local.get(int(dragging_e_idx))
                except Exception:
                    ends = None
            if isinstance(ends, dict):
                fr = ends.get('from'); to = ends.get('to')
                if fr:
                    _draw_handle(fr, filled=(getattr(model, 'dragging_edge_end', None) == 'from'))
                if to:
                    _draw_handle(to, filled=(getattr(model, 'dragging_edge_end', None) == 'to'))
            # Drag preview: show arrow pointing toward the 'to' end
            if dragging_e_idx is not None or isinstance(dragging_e_id, str):
                end_side = getattr(model, 'dragging_edge_end', None)
                px = getattr(model, 'dragging_edge_preview_x', None)
                py = getattr(model, 'dragging_edge_preview_y', None)
                ends = None
                if isinstance(dragging_e_id, str):
                    ends = self.edge_endpoints_local.get(dragging_e_id)
                if ends is None and dragging_e_idx is not None:
                    try:
                        ends = self.edge_endpoints_local.get(int(dragging_e_idx))
                    except Exception:
                        ends = None
                if end_side in ('from', 'to') and isinstance(px, (int, float)) and isinstance(py, (int, float)) and isinstance(ends, dict):
                    tip_local = W((float(px), float(py)))
                    fixed_local = ends.get('to' if end_side == 'from' else 'from')
                    if fixed_local and tip_local:
                        # Determine start (source) and dest (arrowhead) so that arrow always points to 'to'
                        if end_side == 'from':
                            sx, sy = int(tip_local[0]), int(tip_local[1])     # moving 'from'
                            dx, dy = int(fixed_local[0]), int(fixed_local[1]) # fixed 'to'
                        else:  # dragging 'to'
                            sx, sy = int(fixed_local[0]), int(fixed_local[1]) # fixed 'from'
                            dx, dy = int(tip_local[0]), int(tip_local[1])     # moving 'to'
                        # Draw preview polyline and arrowhead at dest
                        pygame.draw.line(surf, (255, 230, 120), (sx, sy), (dx, dy), 2)
                        vx, vy = (dx - sx), (dy - sy)
                        mag = math.hypot(vx, vy) or 1.0
                        ux, uy = vx / mag, vy / mag
                        head_len = 14
                        head_width = 10
                        bx, by = dx - ux * head_len, dy - uy * head_len
                        pxn, pyn = -uy, ux
                        hw = head_width / 2.0
                        left = (bx + pxn * hw, by + pyn * hw)
                        right = (bx - pxn * hw, by - pyn * hw)
                        pygame.draw.polygon(surf, (255, 230, 120), [left, right, (dx, dy)])
        except Exception:
            pass

        # Border of canvas
        pygame.draw.rect(surf, (95, 95, 105), surf.get_rect(), 2)
        # Inline TextInput overlay (draw on top of canvas contents)
        try:
            # Determine if an edit is active
            edit_node = getattr(model, 'editing_node_id', None)
            edit_edge_idx = getattr(model, 'editing_edge_index', None)
            edit_edge_id = getattr(model, 'editing_edge_id', None)
            target_rect_local = None
            if edit_node is not None:
                target_rect_local = self.node_label_rects.get(edit_node)
            else:
                # Edge editing: resolve by ID first, then index fallback
                if isinstance(edit_edge_id, str):
                    target_rect_local = self.edge_label_rects.get(edit_edge_id)
                if target_rect_local is None and edit_edge_idx is not None:
                    try:
                        target_rect_local = self.edge_label_rects.get(int(edit_edge_idx))
                    except Exception:
                        target_rect_local = None
            if target_rect_local is not None:
                # Ensure widget exists and activated
                if self.text_input is None:
                    # Create default font
                    font = pygame.font.SysFont(None, 18)
                    self.text_input = TextInput(font)
                # Update font size to match label size roughly
                # For node labels we used base_font_size (20), for edge labels ~18
                try:
                    base_font_size = 20 if edit_node is not None else 18
                    self.text_input.font = pygame.font.SysFont(None, base_font_size)
                except Exception:
                    pass
                # Activate with pending/init text if needed
                if self._pending_text_edit is not None:
                    init_text, select_all = self._pending_text_edit
                    try:
                        self.text_input.activate(init_text, select_all=select_all)
                    except Exception:
                        pass
                    self._pending_text_edit = None
                # Draw input at local position
                tx = int(target_rect_local.left)
                ty = int(target_rect_local.top)
                # Slightly inset to center within the rect vertically
                self.text_input.draw(surf, tx, ty, color=(255, 255, 255))
                # Compute absolute screen-space rect for controller click-outside checks
                lr = getattr(self.text_input, 'last_rect', None)
                if isinstance(lr, pygame.Rect):
                    self.text_input_abs_rect = pygame.Rect(x + lr.left, y + lr.top, lr.width, lr.height)
        except Exception:
            self.text_input_abs_rect = None
        # Blit to screen
        screen.blit(surf, (x, y))

        # Legend overlay (drawn AFTER blitting canvas so it's on top), bottom-right corner
        try:
            legend_items = [
                ((160, 80, 200), "Damage"),
                ((220, 100, 180), "Alert"),
                ((90, 200, 220), "Interrupt/External"),
            ]
            lfont = pygame.font.SysFont(None, 16)
            small_font = pygame.font.SysFont(None, 14)
            swatch_w, swatch_h = 14, 8
            gap_x, gap_y = 8, 6
            margin = 8
            if getattr(model, 'legend_collapsed', False):
                # Collapsed pill with a [+] button
                label = "Legend"
                txt = lfont.render(label, True, (210, 210, 215))
                btn_w = txt.get_height() + 6
                btn_h = txt.get_height() + 2
                box_w = btn_w + 6 + txt.get_width() + gap_x
                box_h = max(btn_h, txt.get_height()) + gap_y
                box_x = x + w - margin - box_w
                box_y = y + h - margin - box_h
                bg = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
                bg.fill((20, 20, 24, 230))
                pygame.draw.rect(bg, (95, 95, 105), bg.get_rect(), 1)
                # Button [+]
                btn_rect_local = pygame.Rect(gap_x//2, (box_h - btn_h)//2, btn_w, btn_h)
                pygame.draw.rect(bg, (95, 95, 105), btn_rect_local, border_radius=3)
                plus = small_font.render("+", True, (235, 235, 240))
                pr = plus.get_rect(center=btn_rect_local.center)
                bg.blit(plus, pr)
                # Label
                bg.blit(txt, (btn_rect_local.right + 6, (box_h - txt.get_height())//2))
                # Composite
                screen.blit(bg, (box_x, box_y))
                # Store rects (screen-space)
                self.legend_rect = pygame.Rect(box_x, box_y, box_w, box_h)
                self.legend_button_rect = pygame.Rect(box_x + btn_rect_local.left, box_y + btn_rect_local.top, btn_w, btn_h)
            else:
                # Expanded panel with a minimize button [−]
                header = lfont.render("Legend (special)", True, (200, 200, 210))
                max_item_w = 0
                item_h = max(swatch_h, lfont.get_height())
                for color, label in legend_items:
                    tw = lfont.size(label)[0]
                    max_item_w = max(max_item_w, swatch_w + 6 + tw)
                # Minimize button size
                btn_w = 18
                btn_h = 16
                # Box size
                box_w = max(header.get_width() + btn_w + 6, max_item_w) + gap_x * 2
                box_h = header.get_height() + gap_y + len(legend_items) * (item_h + 2) + gap_y
                # Position bottom-right
                box_x = x + w - margin - box_w
                box_y = y + h - margin - box_h
                bg = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
                bg.fill((20, 20, 24, 230))
                pygame.draw.rect(bg, (95, 95, 105), bg.get_rect(), 1)
                # Header and minimize button
                bg.blit(header, (gap_x, gap_y - 2))
                btn_rect_local = pygame.Rect(box_w - gap_x - btn_w, gap_y - 2, btn_w, btn_h)
                pygame.draw.rect(bg, (95, 95, 105), btn_rect_local, border_radius=3)
                minus = small_font.render("-", True, (235, 235, 240))
                mr = minus.get_rect(center=btn_rect_local.center)
                bg.blit(minus, mr)
                # Items
                iy = gap_y + header.get_height()
                for color, label in legend_items:
                    pygame.draw.rect(bg, color, pygame.Rect(gap_x, iy + (item_h - swatch_h)//2, swatch_w, swatch_h))
                    txt = lfont.render(label, True, (210, 210, 215))
                    bg.blit(txt, (gap_x + swatch_w + 6, iy - 1))
                    iy += item_h + 2
                # Composite
                screen.blit(bg, (box_x, box_y))
                # Store rects (screen-space)
                self.legend_rect = pygame.Rect(box_x, box_y, box_w, box_h)
                self.legend_button_rect = pygame.Rect(box_x + btn_rect_local.left, box_y + btn_rect_local.top, btn_w, btn_h)
        except Exception:
            # Non-fatal if we can't render legend
            self.legend_rect = None
            self.legend_button_rect = None

        # Register blocker so gameplay input under canvas is suppressed
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.canvas_rect)
        except Exception:
            pass

        return self.canvas_rect


__all__ = ["FsmGraphPanelView"]
