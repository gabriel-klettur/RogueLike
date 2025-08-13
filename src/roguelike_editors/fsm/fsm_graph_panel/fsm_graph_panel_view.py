from __future__ import annotations


class FsmGraphPanelView:
    def __init__(self) -> None:
        self.canvas_rect = None

    def render(self, model, screen, *, anchor=(360, 120)):
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
        surf.fill((15, 15, 18, 225))
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
            for gx in range(-ox, w, grid):
                pygame.draw.line(surf, grid_color, (gx, 0), (gx, h), 1)
            for gy in range(-oy, h, grid):
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
                sx, sy, sw, sh = node_pos[fr]
                tx, ty, tw, th = node_pos[to]
                sc = (sx + sw/2.0, sy + sh/2.0)
                tc = (tx + tw/2.0, ty + th/2.0)

                color = e.get('color', (120, 120, 140))
                if e.get('active'):
                    color = (255, 210, 90)
                width = int(e.get('width', 2))
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
                    label = e.get('label') or e.get('on') or e.get('event')
                    if label:
                        font = pygame.font.SysFont(None, 18)
                        mid = _quad_point(p0, ctrl, p2, 0.35)
                        mid = W(mid)
                        txt = font.render(str(label), True, (210,210,210))
                        tr = txt.get_rect(center=(mid[0], mid[1]))
                        surf.blit(txt, tr)
                    continue

                pair_key = tuple(sorted([fr, to]))
                dkey = (fr, to)
                idx = dir_index.get(dkey, 0)
                dir_index[dkey] = idx + 1

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
                    sign = 1 if (idx % 2 == 0) else -1
                    mult = (idx // 2) + 1
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
                    label = e.get('label') or e.get('on') or e.get('event')
                    if label:
                        font = pygame.font.SysFont(None, 18)
                        mid_lbl = _quad_point(p_start, ctrl, p_end, 0.5)
                        mid_lbl = W(mid_lbl)
                        txt = font.render(str(label), True, (210,210,210))
                        tr = txt.get_rect(center=(mid_lbl[0], mid_lbl[1]))
                        surf.blit(txt, tr)
                else:
                    # Straight arrow with edge-anchored endpoints
                    dx, dy = (p_end[0]-p_start[0], p_end[1]-p_start[1])
                    p_start_l = W(p_start)
                    p_end_l = W(p_end)
                    _draw_polyline(surf, color, [p_start_l, (p_end_l[0]-dx*0.0001*zoom, p_end_l[1]-dy*0.0001*zoom)], width)
                    _arrowhead(surf, color, p_end_l, (dx*zoom, dy*zoom), head_len=head_len, head_width=head_width)
                    label = e.get('label') or e.get('on') or e.get('event')
                    if label:
                        font = pygame.font.SysFont(None, 18)
                        mid_lbl = ((p_start[0]+p_end[0])/2.0, (p_start[1]+p_end[1])/2.0)
                        mid_lbl = W(mid_lbl)
                        txt = font.render(str(label), True, (210,210,210))
                        tr = txt.get_rect(center=(mid_lbl[0], mid_lbl[1]))
                        surf.blit(txt, tr)
        except Exception:
            pass

        # Nodes
        try:
            font = pygame.font.SysFont(None, 20)
            for n in getattr(model, 'nodes', []):
                nx = int(n.get('x', 0)); ny = int(n.get('y', 0))
                nw = int(n.get('w', 120)); nh = int(n.get('h', 60))
                tl = W((nx, ny))
                rect = pygame.Rect(tl[0], tl[1], int(nw*zoom), int(nh*zoom))
                # body
                pygame.draw.rect(surf, (40, 44, 52), rect, 0, border_radius=6)
                # border (initial highlighted)
                if n.get('id') == getattr(model, 'selected_node_id', None):
                    color = (255, 210, 90)
                    border_w = 3
                elif n.get('initial'):
                    color = (90, 170, 255)
                    border_w = 3
                else:
                    color = (90, 90, 100)
                    border_w = 2
                pygame.draw.rect(surf, color, rect, border_w, border_radius=6)
                # label
                label = str(n.get('label', n.get('id', '?')))
                txt = font.render(label, True, (235, 235, 235))
                tr = txt.get_rect(center=(rect.centerx, rect.centery))
                surf.blit(txt, tr)
        except Exception:
            pass

        # Border of canvas
        pygame.draw.rect(surf, (95, 95, 105), surf.get_rect(), 2)
        # Blit to screen
        screen.blit(surf, (x, y))

        # Register blocker so gameplay input under canvas is suppressed
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.canvas_rect)
        except Exception:
            pass

        return self.canvas_rect


__all__ = ["FsmGraphPanelView"]
