from __future__ import annotations

from typing import List, Optional, Tuple
import re
import pygame

from ..model import DiagnosticsOverlayModel
from ..view import DiagnosticsOverlayView
from ..types import CameraLike, StateLike, MapManagerLike, EntitiesLike
from . import perf_tree as perf
from . import probes


def build_lines(
    model: DiagnosticsOverlayModel,
    view: DiagnosticsOverlayView,
    state: Optional[StateLike] = None,
    camera: Optional[CameraLike] = None,
    map_manager: Optional[MapManagerLike] = None,
    entities: Optional[EntitiesLike] = None,
    extra_lines: Optional[List[str]] = None,
) -> Tuple[List[Tuple[str, str]], int, int, List[Optional[int]]]:
    """Compose the diagnostics text lines and layout widths.

    Returns (lines, label_w, value_w, line_levels) where lines are (left,right) tuples
    and line_levels indicates indentation level (or None for non-hierarchical rows).
    """
    lines: List[Tuple[str, str]] = []
    line_levels: List[Optional[int]] = []
    label_w = value_w = 0

    tree = perf.build_perf_tree(model.perf_log)
    if model.initially_collapsed:
        model.collapsed_groups = set(perf.collect_group_ids(tree))
        model.initially_collapsed = False

    def render_node(node, level: int = 0):
        # Render child groups first (with headers), flattening single-item subtrees into item lines
        for gid in sorted(node["children"].keys(), key=perf.numeric_sort_key):
            child = node["children"][gid]
            sole = perf.find_sole_item(child)
            if sole:
                full_gid, label, avg_ms = sole
                # Compose label without duplicating the id
                if full_gid and (not label or label.startswith(full_gid)):
                    display_label = full_gid
                elif full_gid:
                    display_label = f"{full_gid} {label}"
                else:
                    display_label = label
                # No header -> do not add an extra indent level
                lbl = f"{'  ' * level}{display_label:<20}"
                val = f"{avg_ms:>6.2f} ms"
                lines.append((lbl, val))
                line_levels.append(level)
                continue
            # Render header for multi-item groups (header indentation is only visual)
            name_part = f" {child.get('title', '').strip()}" if child.get('title') else ""
            is_collapsed = gid in model.collapsed_groups
            indicator = '▶' if is_collapsed else '▼'
            header_label = f"{indicator} {gid}{name_part}:"
            lbl = f"{'  ' * level}{header_label}"
            val = f"{child.get('total', 0.0):>6.2f} ms ({child.get('count', 0)})"
            lines.append((lbl, val))
            line_levels.append(level)
            if is_collapsed:
                continue
            # Render child subtree
            render_node(child, level + 1)
            # Then render items directly under this node
            for item_id, label, avg_ms in sorted(child["items"], key=lambda it: (it[0] is None, it[0] or "", it[1])):
                display_id = item_id if perf.is_numeric_id(item_id) else (child.get('id') if perf.is_numeric_id(child.get('id')) else None)
                display_label = f"{display_id} {label}".strip() if display_id else label
                lbl = f"{'  ' * level}{display_label:<20}"
                val = f"{avg_ms:>6.2f} ms"
                lines.append((lbl, val))
                line_levels.append(level)

    render_node(tree, 0)

    # Build a set of normalized labels from loop-generated lines to avoid duplicates
    def _norm(lbl: str) -> str:
        s = (lbl or "").strip()
        # Remove expand/collapse indicators
        s = re.sub(r'^[▶▼]\s*', '', s)
        # Remove numeric dotted prefixes
        s = re.sub(r'^(\d+(?:\.\d+)*)\s*', '', s)
        # Drop trailing colon
        s = s.rstrip(':')
        # Collapse internal whitespace
        s = re.sub(r'\s+', ' ', s)
        return s

    existing_norms = {_norm(l) for (l, _r) in lines}

    if state and hasattr(state, 'clock'):
        fps = state.clock.get_fps()
        ft = (1000 / fps) if fps > 0 else 0
        if _norm("FrameTime:") not in existing_norms:
            lines.insert(0, ("FrameTime:", f"{ft:0.1f} ms"))
            line_levels.insert(0, None)
        if _norm("FPS:") not in existing_norms:
            lines.insert(0, ("FPS:", f"{fps:0.1f}"))
            line_levels.insert(0, None)

    if extra_lines is None and state and camera and map_manager and entities:
        extra_lines = probes.get_custom_debug_lines(state, camera, map_manager, entities)
    if extra_lines:
        # Filter out custom/manual lines that duplicate existing loop-generated labels
        filtered = []
        for text in extra_lines:
            if _norm(text) not in existing_norms:
                filtered.append(text)
        if filtered:
            lines.append(("", ""))
            line_levels.append(None)
            for text in filtered:
                lines.append((text, ""))
                line_levels.append(None)

    # Safety: limit number of lines only when paging is disabled
    if not getattr(model, 'paging_enabled', False):
        max_lines = getattr(model, 'max_lines', 400)
        if len(lines) > max_lines:
            truncated_count = len(lines) - max_lines
            keep = max_lines - 1 if max_lines >= 1 else 0
            if keep > 0:
                lines = lines[:keep] + [("...", f"{truncated_count} líneas ocultas")]
                line_levels = line_levels[:keep] + [None]
            else:
                lines = [("...", f"{truncated_count} líneas ocultas")]
                line_levels = [None]

    # Truncate fields to avoid creating huge text surfaces. Do NOT truncate header labels (ending with ':').
    def _truncate_field(left: str, right: str) -> tuple[str, str]:
        max_chars = getattr(model, 'max_chars_per_field', 256)
        l = left
        # Respect headers (used for group ids and interaction)
        if not left.strip().endswith(':') and len(left) > max_chars:
            l = left[: max_chars - 1] + '…'
        r = right
        if len(right) > max_chars:
            r = right[: max_chars - 1] + '…'
        return l, r

    lines = [_truncate_field(l, r) for (l, r) in lines]

    # Final width adjust for all lines (single pass) using possibly truncated values
    font = view._get_font(model.font_name, model.font_size)
    for left, right in lines:
        lw, _ = font.size(left)
        vw, _ = font.size(right)
        label_w = max(label_w, lw)
        value_w = max(value_w, vw)

    return lines, label_w, value_w, line_levels
