from __future__ import annotations
from typing import Any, Callable, Dict, List, Tuple

try:
    import pygame  # type: ignore
    import math
except Exception:  # pragma: no cover - safe import guard
    pygame = None  # type: ignore
    math = None  # type: ignore

from ._edge_utils import (
    build_node_pos,
    dominant_side,
    edge_point_with_slot,
    bezier_samples,
    quad_point,
    group_edges,
    Point,
)


def _draw_polyline(surface, color, points, width=2) -> None:
    if pygame is None:
        return
    if len(points) >= 2:
        pygame.draw.lines(surface, color, False, points, width)


def _arrowhead(surface, color, tip, direction, *, head_len=14, head_width=10) -> None:
    if pygame is None or math is None:
        return
    vx, vy = direction
    mag = math.hypot(vx, vy)
    if mag < 1e-3:
        return
    ux, uy = vx / mag, vy / mag
    bx = tip[0] - ux * head_len
    by = tip[1] - uy * head_len
    px, py = -uy, ux
    hw = head_width / 2.0
    p_left = (bx + px * hw, by + py * hw)
    p_right = (bx - px * hw, by - py * hw)
    pygame.draw.polygon(surface, color, [p_left, p_right, tip])


def _badge_surface(text: str, color_bg: Tuple[int, int, int]):
    if pygame is None:
        return None
    f = pygame.font.SysFont(None, 14)
    t = f.render(text, True, (255, 255, 255))
    pad_x, pad_y = 4, 2
    bw, bh = t.get_width() + pad_x * 2, t.get_height() + pad_y * 2
    s = pygame.Surface((bw, bh), pygame.SRCALPHA)
    s.fill((*color_bg, 230))
    pygame.draw.rect(s, (255, 255, 255), s.get_rect(), 1, border_radius=6)
    s.blit(t, (pad_x, pad_y))
    return s


def _edge_style(model: Any, e: Dict[str, Any], idx: int) -> Tuple[Tuple[int, int, int], int, int, int, bool, bool]:
    eid = e.get("id")
    is_hover = (idx == getattr(model, "hover_edge_index", None)) or (
        isinstance(eid, str) and eid == getattr(model, "hover_edge_id", None)
    )
    is_selected = (idx == getattr(model, "selected_edge_index", None)) or (
        isinstance(eid, str) and eid == getattr(model, "selected_edge_id", None)
    )
    color = e.get("color", (120, 120, 140))
    if e.get("active"):
        color = (255, 210, 90)
    elif is_selected:
        color = (255, 220, 110)
    elif is_hover:
        color = (255, 230, 120)
    width = int(e.get("width", 2))
    if is_selected:
        width = max(width + 2, 4)
    elif is_hover:
        width = max(width + 1, 3)
    head_len = int(e.get("head_len", 14))
    head_width = int(e.get("head_width", 10))
    return color, width, head_len, head_width, is_hover, is_selected


def _label_text(model: Any, e: Dict[str, Any], idx: int) -> Tuple[str, bool, bool]:
    eid = e.get("id")
    is_editing = (getattr(model, "editing_edge_index", None) == idx) or (
        isinstance(eid, str) and getattr(model, "editing_edge_id", None) == eid
    )
    label = e.get("label") or e.get("on") or e.get("event")
    text_for_rect = str(getattr(model, "editing_text", "") or "") if is_editing else str(label or "")
    return text_for_rect, bool(label), is_editing


def _label_draw_and_store(
    model: Any,
    view: Any,
    key: Any,
    idx: int,
    surf: Any,
    text: str,
    center: Tuple[int, int],
    focus_on: bool,
) -> None:
    if pygame is None:
        return
    font = pygame.font.SysFont(None, 20 if focus_on else 18)
    txt = font.render(text, True, (255, 230, 120) if focus_on else (210, 210, 210))
    tr = txt.get_rect(center=center)
    surf.blit(txt, tr)
    try:
        view.edge_label_rects[idx] = tr.copy()
        if isinstance(key, str):
            view.edge_label_rects[key] = tr.copy()
    except Exception:
        pass


def _badges_draw_and_store(view: Any, surf: Any, key: Any, center: Tuple[int, int], items: List[Dict[str, Any]] | None) -> None:
    if pygame is None:
        return
    if items is None:
        # For int keys, legacy default empty list in original logic
        items = []
    if not items:
        return
    errs = [it for it in items if it.get("severity") == "error"]
    warns = [it for it in items if it.get("severity") == "warning"]
    if not (errs or warns):
        return
    cx = int(center[0])
    cy = int(center[1])
    rmap: Dict[str, Any] = {}
    if errs:
        b = _badge_surface(str(len(errs)), (200, 60, 60))
        if b is not None:
            br = b.get_rect(); br.center = (cx, cy)
            surf.blit(b, br)
            rmap["error"] = br
            cx = br.right + 4
    if warns:
        b = _badge_surface(str(len(warns)), (220, 160, 60))
        if b is not None:
            br = b.get_rect(); br.center = (cx, cy)
            surf.blit(b, br)
            rmap["warning"] = br
    try:
        view.edge_badge_rects[key] = rmap
    except Exception:
        pass


def draw_all_edges(model: Any, surf: Any, W: Callable[[Tuple[float, float]], Tuple[int, int]], view: Any) -> None:
    if pygame is None or math is None:
        return None
    try:
        node_pos = build_node_pos(model)
        edges: List[Dict[str, Any]] = list(getattr(model, "edges", []))
        pair_counts, src_groups, dst_groups = group_edges(edges, node_pos)
        dir_index: Dict[Tuple[Any, Any], int] = {}

        view.edge_paths = {}
        view.edge_endpoints_local = {}
        view.edge_label_rects = {}
        view.edge_badge_rects = {}

        for idx, e in enumerate(edges):
            fr = e.get("from"); to = e.get("to")
            if fr not in node_pos or to not in node_pos:
                continue
            sx, sy, sw, sh = node_pos[fr]
            tx, ty, tw, th = node_pos[to]
            sc = (sx + sw / 2.0, sy + sh / 2.0)
            tc = (tx + tw / 2.0, ty + th / 2.0)

            color, width, head_len, head_width, is_hover, is_selected = _edge_style(model, e, idx)
            eid = e.get("id")

            if fr == to:
                loop_h = max(sh, 60)
                p0 = (sx + sw / 2.0, sy)
                p2 = p0
                ctrl = (sc[0] + sw * 0.8, sy - loop_h)
                pts = [W(p) for p in bezier_samples(p0, ctrl, p2, 18)]
                if len(pts) >= 2:
                    p_tip = pts[-1]
                    p_prev = pts[-2]
                    dir_vec = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
                    _draw_polyline(surf, color, pts[:-1], width)
                    _arrowhead(surf, color, p_tip, dir_vec, head_len=head_len, head_width=head_width)
                try:
                    lp = W(p0)
                    view.edge_endpoints_local[idx] = {"from": lp, "to": lp}
                    if isinstance(eid, str):
                        view.edge_endpoints_local[eid] = {"from": lp, "to": lp}
                except Exception:
                    pass
                try:
                    view.edge_paths[idx] = list(pts)
                    if isinstance(eid, str):
                        view.edge_paths[eid] = list(pts)
                except Exception:
                    pass
                label = e.get("label") or e.get("on") or e.get("event")
                is_focus = is_hover or is_selected
                font = pygame.font.SysFont(None, 20 if is_focus else 18)
                mid = quad_point(p0, ctrl, p2, 0.35)
                mid_l = W(mid)
                is_editing = (getattr(model, "editing_edge_index", None) == idx) or (
                    isinstance(eid, str) and getattr(model, "editing_edge_id", None) == eid
                )
                if label or is_editing:
                    text_for_rect = str(getattr(model, "editing_text", "") or "") if is_editing else str(label or "")
                    txt = font.render(text_for_rect, True, (255, 230, 120) if is_focus else (210, 210, 210))
                    tr = txt.get_rect(center=(mid_l[0], mid_l[1]))
                    if not is_editing:
                        surf.blit(txt, tr)
                    try:
                        view.edge_label_rects[idx] = tr.copy()
                        if isinstance(eid, str):
                            view.edge_label_rects[eid] = tr.copy()
                    except Exception:
                        pass
                try:
                    key = eid if isinstance(eid, str) else idx
                    items = (getattr(model, "edge_lint_by_id", {}) or {}).get(eid) if isinstance(eid, str) else []
                    if items:
                        cx = int(mid_l[0] + 6)
                        cy = int(mid_l[1] - 6)
                        _badges_draw_and_store(view, surf, key, (cx, cy), items)
                except Exception:
                    pass
                continue

            pair_key = tuple(sorted([fr, to]))
            dkey = (fr, to)
            dir_i = dir_index.get(dkey, 0)
            dir_index[dkey] = dir_i + 1

            sdx, sdy = (tc[0] - sc[0], tc[1] - sc[1])
            ddx, ddy = (sc[0] - tc[0], sc[1] - tc[1])
            s_side = dominant_side(sdx, sdy)
            d_side = dominant_side(ddx, ddy)
            s_list = src_groups.get((fr, s_side), [idx])
            d_list = dst_groups.get((to, d_side), [idx])
            s_idx = s_list.index(idx) if idx in s_list else 0
            d_idx = d_list.index(idx) if idx in d_list else 0
            p_start = edge_point_with_slot((sx, sy, sw, sh), tc, s_idx, len(s_list))
            p_end = edge_point_with_slot((tx, ty, tw, th), sc, d_idx, len(d_list))

            total_in_pair = pair_counts.get(pair_key, 1)
            need_curve = total_in_pair > 1 or e.get("curved")
            if need_curve:
                dx, dy = (p_end[0] - p_start[0], p_end[1] - p_start[1])
                dlen = math.hypot(dx, dy) or 1.0
                nx, ny = -dy / dlen, dx / dlen
                step = float(e.get("curve_step", 24))
                ux, uy = dx / dlen, dy / dlen
                alignment = max(abs(ux), abs(uy))
                step *= (1.0 + 0.75 * alignment)
                sign = 1 if (dir_i % 2 == 0) else -1
                mult = (dir_i // 2) + 1
                offset = sign * mult * step
                mid = ((p_start[0] + p_end[0]) / 2.0, (p_start[1] + p_end[1]) / 2.0)
                ctrl = (mid[0] + nx * offset, mid[1] + ny * offset)
                pts = [W(p) for p in bezier_samples(p_start, ctrl, p_end, 18)]
                if len(pts) >= 2:
                    p_tip = pts[-1]
                    p_prev = pts[-2]
                    dir_vec = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
                    _draw_polyline(surf, color, pts[:-1], width)
                    _arrowhead(surf, color, p_tip, dir_vec, head_len=head_len, head_width=head_width)
                try:
                    view.edge_endpoints_local[idx] = {"from": W(p_start), "to": W(p_end)}
                    if isinstance(eid, str):
                        view.edge_endpoints_local[eid] = {"from": W(p_start), "to": W(p_end)}
                except Exception:
                    pass
                try:
                    view.edge_paths[idx] = list(pts)
                    if isinstance(eid, str):
                        view.edge_paths[eid] = list(pts)
                except Exception:
                    pass
                label = e.get("label") or e.get("on") or e.get("event")
                is_editing = (getattr(model, "editing_edge_index", None) == idx) or (
                    isinstance(eid, str) and getattr(model, "editing_edge_id", None) == eid
                )
                if label or is_editing:
                    is_focus = is_hover or is_selected
                    font = pygame.font.SysFont(None, 20 if is_focus else 18)
                    mid_lbl = quad_point(p_start, ctrl, p_end, 0.5)
                    mid_lbl_l = W(mid_lbl)
                    text_for_rect = str(getattr(model, "editing_text", "") or "") if is_editing else str(label or "")
                    txt = font.render(text_for_rect, True, (255, 230, 120) if is_focus else (210, 210, 210))
                    tr = txt.get_rect(center=(mid_lbl_l[0], mid_lbl_l[1]))
                    if not is_editing:
                        surf.blit(txt, tr)
                    try:
                        view.edge_label_rects[idx] = tr.copy()
                        if isinstance(eid, str):
                            view.edge_label_rects[eid] = tr.copy()
                    except Exception:
                        pass
                try:
                    key = eid if isinstance(eid, str) else idx
                    items = (getattr(model, "edge_lint_by_id", {}) or {}).get(eid) if isinstance(eid, str) else []
                    if items:
                        cx = int(mid_lbl_l[0] + 8)
                        cy = int(mid_lbl_l[1] - 10)
                        _badges_draw_and_store(view, surf, key, (cx, cy), items)
                except Exception:
                    pass
            else:
                p_start_l = W(p_start)
                p_end_l = W(p_end)
                pts = [p_start_l, p_end_l]
                if len(pts) >= 2:
                    p_tip = pts[-1]
                    p_prev = pts[-2]
                    vx, vy = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
                    mag = math.hypot(vx, vy) or 1.0
                    retract = 0.001 * mag
                    ux, uy = vx / mag, vy / mag
                    shortened_tip = (p_tip[0] - ux * retract, p_tip[1] - uy * retract)
                    _draw_polyline(surf, color, [pts[0], shortened_tip], width)
                    dir_vec = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
                    _arrowhead(surf, color, p_tip, dir_vec, head_len=head_len, head_width=head_width)
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
                label = e.get("label") or e.get("on") or e.get("event")
                is_editing = (getattr(model, "editing_edge_index", None) == idx) or (
                    isinstance(eid, str) and getattr(model, "editing_edge_id", None) == eid
                )
                is_focus = is_hover or is_selected
                font = pygame.font.SysFont(None, 20 if is_focus else 18)
                mid_lbl = ((p_start[0] + p_end[0]) / 2.0, (p_start[1] + p_end[1]) / 2.0)
                mid_lbl_l = W(mid_lbl)
                if label or is_editing:
                    text_for_rect = str(getattr(model, "editing_text", "") or "") if is_editing else str(label or "")
                    txt = font.render(text_for_rect, True, (255, 230, 120) if is_focus else (210, 210, 210))
                    tr = txt.get_rect(center=(mid_lbl_l[0], mid_lbl_l[1]))
                    if not is_editing:
                        surf.blit(txt, tr)
                    try:
                        view.edge_label_rects[idx] = tr.copy()
                        if isinstance(eid, str):
                            view.edge_label_rects[eid] = tr.copy()
                    except Exception:
                        pass
                try:
                    key = eid if isinstance(eid, str) else idx
                    items = (getattr(model, "edge_lint_by_id", {}) or {}).get(eid) if isinstance(eid, str) else []
                    if items:
                        cx = int(mid_lbl_l[0] + 8)
                        cy = int(mid_lbl_l[1] - 10)
                        _badges_draw_and_store(view, surf, key, (cx, cy), items)
                except Exception:
                    pass
    except Exception:
        pass
