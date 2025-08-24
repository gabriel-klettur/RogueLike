from __future__ import annotations
from typing import Any, Callable


def draw_edges(model: Any, surf: Any, W: Callable[[tuple[float, float]], tuple[int, int]], view: Any) -> None:
    try:
        import pygame  # type: ignore
        import math
    except Exception:
        return None
    try:
        node_pos = {n['id']: (int(n.get('x', 0)), int(n.get('y', 0)), int(n.get('w', 120)), int(n.get('h', 60))) for n in getattr(model, 'nodes', [])}

        def _dominant_side(dx: float, dy: float) -> str:
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

        def _quad_point(p0, p1, p2, t: float):
            it = 1.0 - t
            return (
                it*it*p0[0] + 2*it*t*p1[0] + t*t*p2[0],
                it*it*p0[1] + 2*it*t*p1[1] + t*t*p2[1],
            )

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
        pair_counts: dict[tuple, int] = {}
        dir_counts: dict[tuple, int] = {}
        # Group edges per node side for port distribution
        src_groups: dict[tuple, list[int]] = {}
        dst_groups: dict[tuple, list[int]] = {}
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
        dir_index: dict[tuple, int] = {}

        view.edge_paths = {}
        view.edge_endpoints_local = {}
        view.edge_label_rects = {}
        view.edge_badge_rects = {}

        for idx, e in enumerate(edges):
            fr = e.get('from'); to = e.get('to')
            if fr not in node_pos or to not in node_pos:
                continue
            sx, sy, sw, sh = node_pos[fr]
            tx, ty, tw, th = node_pos[to]
            sc = (sx + sw/2.0, sy + sh/2.0)
            tc = (tx + tw/2.0, ty + th/2.0)
            # Resolve hover/selection state using edge ID or index
            eid = e.get('id')
            is_edge_hover = (idx == getattr(model, 'hover_edge_index', None)) or (isinstance(eid, str) and eid == getattr(model, 'hover_edge_id', None))
            is_edge_selected = (idx == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and eid == getattr(model, 'selected_edge_id', None))
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
                    view.edge_endpoints_local[idx] = {"from": lp, "to": lp}
                    if isinstance(eid, str):
                        view.edge_endpoints_local[eid] = {"from": lp, "to": lp}
                except Exception:
                    pass
                # Store path for hover proximity
                try:
                    view.edge_paths[idx] = list(pts)
                    if isinstance(eid, str):
                        view.edge_paths[eid] = list(pts)
                except Exception:
                    pass
                label = e.get('label') or e.get('on') or e.get('event')
                is_editing = (getattr(model, 'editing_edge_index', None) == idx) or (isinstance(eid, str) and getattr(model, 'editing_edge_id', None) == eid)
                # Compute a midpoint for label/badge placement on the loop
                is_hover = (idx == getattr(model, 'hover_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'hover_edge_id', None) == eid)
                is_selected = (idx == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'selected_edge_id', None) == eid)
                is_focus = is_hover or is_selected
                font = pygame.font.SysFont(None, 20 if is_focus else 18)
                mid = _quad_point(p0, ctrl, p2, 0.35)
                mid = W(mid)
                # Label (skip draw if editing)
                if label or is_editing:
                    text_for_rect = str(getattr(model, 'editing_text', '') or '') if is_editing else str(label or '')
                    txt = font.render(text_for_rect, True, (255,230,120) if is_focus else (210,210,210))
                    tr = txt.get_rect(center=(mid[0], mid[1]))
                    if not is_editing:
                        surf.blit(txt, tr)
                    try:
                        view.edge_label_rects[idx] = tr.copy()
                        if isinstance(eid, str):
                            view.edge_label_rects[eid] = tr.copy()
                    except Exception:
                        pass
                # Per-edge badges near loop midpoint
                try:
                    key = eid if isinstance(eid, str) else idx
                    items = (getattr(model, 'edge_lint_by_id', {}) or {}).get(eid) if isinstance(eid, str) else None
                    if items is None and not isinstance(eid, str):
                        items = []
                    if items:
                        errs = [it for it in items if it.get('severity') == 'error']
                        warns = [it for it in items if it.get('severity') == 'warning']
                        if errs or warns:
                            def _badge(text: str, color_bg: tuple[int,int,int]):
                                f = pygame.font.SysFont(None, 14)
                                t = f.render(text, True, (255, 255, 255))
                                pad_x, pad_y = 4, 2
                                bw, bh = t.get_width() + pad_x * 2, t.get_height() + pad_y * 2
                                s = pygame.Surface((bw, bh), pygame.SRCALPHA)
                                s.fill((*color_bg, 230))
                                pygame.draw.rect(s, (255, 255, 255), s.get_rect(), 1, border_radius=6)
                                s.blit(t, (pad_x, pad_y))
                                return s
                            cx = int(mid[0] + 6)
                            cy = int(mid[1] - 6)
                            rmap = {}
                            if errs:
                                b = _badge(str(len(errs)), (200, 60, 60))
                                br = b.get_rect(); br.center = (cx, cy)
                                surf.blit(b, br)
                                rmap['error'] = br
                                cx = br.right + 4
                            if warns:
                                b = _badge(str(len(warns)), (220, 160, 60))
                                br = b.get_rect(); br.center = (cx, cy)
                                surf.blit(b, br)
                                rmap['warning'] = br
                            try:
                                view.edge_badge_rects[key] = rmap
                            except Exception:
                                pass
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
                    view.edge_endpoints_local[idx] = {"from": W(p_start), "to": W(p_end)}
                    if isinstance(eid, str):
                        view.edge_endpoints_local[eid] = {"from": W(p_start), "to": W(p_end)}
                except Exception:
                    pass
                # Store path for hover proximity
                try:
                    view.edge_paths[idx] = list(pts)
                    if isinstance(eid, str):
                        view.edge_paths[eid] = list(pts)
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
                        view.edge_label_rects[idx] = tr.copy()
                        if isinstance(eid, str):
                            view.edge_label_rects[eid] = tr.copy()
                    except Exception:
                        pass
                # Per-edge badges near mid label
                try:
                    key = eid if isinstance(eid, str) else idx
                    items = (getattr(model, 'edge_lint_by_id', {}) or {}).get(eid) if isinstance(eid, str) else None
                    if items is None and not isinstance(eid, str):
                        items = []
                    if items:
                        errs = [it for it in items if it.get('severity') == 'error']
                        warns = [it for it in items if it.get('severity') == 'warning']
                        if errs or warns:
                            def _badge(text: str, color_bg: tuple[int,int,int]):
                                f = pygame.font.SysFont(None, 14)
                                t = f.render(text, True, (255, 255, 255))
                                pad_x, pad_y = 4, 2
                                bw, bh = t.get_width() + pad_x * 2, t.get_height() + pad_y * 2
                                s = pygame.Surface((bw, bh), pygame.SRCALPHA)
                                s.fill((*color_bg, 230))
                                pygame.draw.rect(s, (255, 255, 255), s.get_rect(), 1, border_radius=6)
                                s.blit(t, (pad_x, pad_y))
                                return s
                            cx = int(mid_lbl[0] + 8)
                            cy = int(mid_lbl[1] - 10)
                            rmap = {}
                            if errs:
                                b = _badge(str(len(errs)), (200, 60, 60))
                                br = b.get_rect(); br.center = (cx, cy)
                                surf.blit(b, br)
                                rmap['error'] = br
                                cx = br.right + 4
                            if warns:
                                b = _badge(str(len(warns)), (220, 160, 60))
                                br = b.get_rect(); br.center = (cx, cy)
                                surf.blit(b, br)
                                rmap['warning'] = br
                            try:
                                view.edge_badge_rects[key] = rmap
                            except Exception:
                                pass
                except Exception:
                    pass
            else:
                # Straight edge drawing
                p_start_l = W(p_start)
                p_end_l = W(p_end)
                pts = [p_start_l, p_end_l]
                if len(pts) >= 2:
                    p_tip = pts[-1]
                    p_prev = pts[-2]
                    vx, vy = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
                    mag = math.hypot(vx, vy) or 1.0
                    # retract a tiny amount to avoid overdraw under the arrowhead
                    retract = 0.001 * mag
                    ux, uy = vx / mag, vy / mag
                    shortened_tip = (p_tip[0] - ux * retract, p_tip[1] - uy * retract)
                    _draw_polyline(surf, color, [pts[0], shortened_tip], width)
                    dir_vec = (p_tip[0]-p_prev[0], p_tip[1]-p_prev[1])
                    _arrowhead(surf, color, p_tip, dir_vec, head_len=head_len, head_width=head_width)
                # Cache endpoints and path
                try:
                    view.edge_endpoints_local[idx] = {"from": p_start_l, "to": p_end_l}
                    if isinstance(eid, str):
                        view.edge_endpoints_local[eid] = {"from": p_start_l, "to": p_end_l}
                except Exception:
                    pass
                try:
                    view.edge_paths[idx] = list(pts)
                    if isinstance(eid, str):
                        view.edge_paths[eid] = list(pts)
                except Exception:
                    pass
                # Label at midpoint (skip while editing)
                label = e.get('label') or e.get('on') or e.get('event')
                is_editing = (getattr(model, 'editing_edge_index', None) == idx) or (isinstance(eid, str) and getattr(model, 'editing_edge_id', None) == eid)
                is_hover = (idx == getattr(model, 'hover_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'hover_edge_id', None) == eid)
                is_selected = (idx == getattr(model, 'selected_edge_index', None)) or (isinstance(eid, str) and getattr(model, 'selected_edge_id', None) == eid)
                is_focus = is_hover or is_selected
                font = pygame.font.SysFont(None, 20 if is_focus else 18)
                mid_lbl = ((p_start[0]+p_end[0])/2.0, (p_start[1]+p_end[1])/2.0)
                mid_lbl = W(mid_lbl)
                if label or is_editing:
                    text_for_rect = str(getattr(model, 'editing_text', '') or '') if is_editing else str(label or '')
                    txt = font.render(text_for_rect, True, (255,230,120) if is_focus else (210,210,210))
                    tr = txt.get_rect(center=(mid_lbl[0], mid_lbl[1]))
                    if not is_editing:
                        surf.blit(txt, tr)
                    try:
                        view.edge_label_rects[idx] = tr.copy()
                        if isinstance(eid, str):
                            view.edge_label_rects[eid] = tr.copy()
                    except Exception:
                        pass
                # Per-edge badges near mid label
                try:
                    key = eid if isinstance(eid, str) else idx
                    items = (getattr(model, 'edge_lint_by_id', {}) or {}).get(eid) if isinstance(eid, str) else None
                    if items is None and not isinstance(eid, str):
                        items = []
                    if items:
                        errs = [it for it in items if it.get('severity') == 'error']
                        warns = [it for it in items if it.get('severity') == 'warning']
                        if errs or warns:
                            def _badge(text: str, color_bg: tuple[int,int,int]):
                                f = pygame.font.SysFont(None, 14)
                                t = f.render(text, True, (255, 255, 255))
                                pad_x, pad_y = 4, 2
                                bw, bh = t.get_width() + pad_x * 2, t.get_height() + pad_y * 2
                                s = pygame.Surface((bw, bh), pygame.SRCALPHA)
                                s.fill((*color_bg, 230))
                                pygame.draw.rect(s, (255, 255, 255), s.get_rect(), 1, border_radius=6)
                                s.blit(t, (pad_x, pad_y))
                                return s
                            cx = int(mid_lbl[0] + 8)
                            cy = int(mid_lbl[1] - 10)
                            rmap = {}
                            if errs:
                                b = _badge(str(len(errs)), (200, 60, 60))
                                br = b.get_rect(); br.center = (cx, cy)
                                surf.blit(b, br)
                                rmap['error'] = br
                                cx = br.right + 4
                            if warns:
                                b = _badge(str(len(warns)), (220, 160, 60))
                                br = b.get_rect(); br.center = (cx, cy)
                                surf.blit(b, br)
                                rmap['warning'] = br
                            try:
                                view.edge_badge_rects[key] = rmap
                            except Exception:
                                pass
                except Exception:
                    pass
    except Exception:
        pass


def redraw_hovered_edge(model: Any, surf: Any, view: Any) -> None:
    try:
        import pygame  # type: ignore
        import math
    except Exception:
        return None
    try:
        hover_eid = getattr(model, 'hover_edge_id', None)
        hover_ei = getattr(model, 'hover_edge_index', None)
        # Resolve key to fetch cached path (prefer ID)
        key = None
        if isinstance(hover_eid, str) and hover_eid in getattr(view, 'edge_paths', {}):
            key = hover_eid
        else:
            try:
                if hover_ei is not None and int(hover_ei) in getattr(view, 'edge_paths', {}):
                    key = int(hover_ei)
            except Exception:
                key = None
        if key is None:
            return None
        pts = view.edge_paths.get(key)
        if not (isinstance(pts, list) and len(pts) >= 2):
            return None
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
        if not isinstance(e, dict):
            return None
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
            pygame.draw.lines(surf, color, False, pts[:-1], width)
        else:
            # Straight 2-point path: draw line to slightly before the tip
            vx, vy = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
            mag = math.hypot(vx, vy) or 1.0
            # retract a tiny amount to avoid overdraw under the arrowhead
            retract = 0.001 * mag
            ux, uy = vx / mag, vy / mag
            shortened_tip = (p_tip[0] - ux * retract, p_tip[1] - uy * retract)
            pygame.draw.lines(surf, color, False, [pts[0], shortened_tip], width)
        dir_vec = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
        pygame.draw.polygon(surf, color, _arrow_points(p_tip, dir_vec, head_len=head_len, head_width=head_width))
        # Re-draw label above others (skip while editing)
        is_editing = (getattr(model, 'editing_edge_index', None) == idx) or (isinstance(eid, str) and getattr(model, 'editing_edge_id', None) == eid)
        if is_editing:
            return None
        label = e.get('label') or e.get('on') or e.get('event')
        if not label:
            return None
        try:
            # Prefer stored label rect center
            lr = view.edge_label_rects.get(key)
            if lr is None and isinstance(idx, int):
                lr = view.edge_label_rects.get(idx)
        except Exception:
            lr = None
        if lr is not None:
            font = pygame.font.SysFont(None, 20 if is_edge_selected else 20)
            txt = font.render(str(label), True, (255, 230, 120))
            tr = txt.get_rect(center=(lr.centerx, lr.centery))
            surf.blit(txt, tr)
    except Exception:
        pass


def _arrow_points(tip, direction, *, head_len=14, head_width=10):
    import math
    vx, vy = direction
    mag = math.hypot(vx, vy) or 1.0
    ux, uy = vx / mag, vy / mag
    bx, by = tip[0] - ux * head_len, tip[1] - uy * head_len
    pxn, pyn = -uy, ux
    hw = head_width / 2.0
    left = (bx + pxn * hw, by + pyn * hw)
    right = (bx - pxn * hw, by - pyn * hw)
    return [left, right, (tip[0], tip[1])]
