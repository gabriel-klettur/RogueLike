from __future__ import annotations

from typing import Any, Tuple
from ..services.hit_test import pick_node_world
from ..model import to_world


def handle_edge_drag_event(controller: Any, model: Any, view: Any, event: Any) -> bool:
    """Handle edge handle drag workflow:
    - LMB down on a hovered handle ('from'/'to'): start drag and store original endpoints
    - Mouse move: update preview world point
    - LMB up: drop on node to reassign endpoint; otherwise revert
    Returns True if consumed.
    """
    try:
        import pygame  # type: ignore
    except Exception:
        return False

    et = getattr(event, 'type', None)
    if et not in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION):
        return False

    rect = getattr(view, 'canvas_rect', None)
    if rect is None:
        return False

    mouse_pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
    inside = rect.collidepoint(mouse_pos)
    local_x = mouse_pos[0] - rect.left
    local_y = mouse_pos[1] - rect.top

    # Start drag when clicking a hovered handle
    if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1 and inside:
        end = getattr(model, 'hover_edge_handle_end', None)
        ei = getattr(model, 'hover_edge_index', None)
        if end in ('from', 'to') and ei is not None:
            try:
                ei_int = int(ei)
            except Exception:
                ei_int = -1
            edges = getattr(model, 'edges', []) or []
            if 0 <= ei_int < len(edges):
                e = edges[ei_int]
                model.dragging_edge_index = ei_int
                # Set id if available via caches
                try:
                    ids = getattr(model, 'edge_id_by_index', []) or []
                    model.dragging_edge_id = ids[ei_int] if 0 <= ei_int < len(ids) else e.get('id')
                except Exception:
                    model.dragging_edge_id = e.get('id')
                model.dragging_edge_end = end
                model.dragging_edge_orig_from = e.get('from')
                model.dragging_edge_orig_to = e.get('to')
                # Initialize preview at current mouse world pos
                wx, wy = to_world(model, local_x, local_y)
                model.dragging_edge_preview_x = float(wx)
                model.dragging_edge_preview_y = float(wy)
                return True

    # Update preview while dragging
    if et == pygame.MOUSEMOTION and (
        getattr(model, 'dragging_edge_index', None) is not None or getattr(model, 'dragging_edge_id', None) is not None
    ):
        wx, wy = to_world(model, local_x, local_y)
        model.dragging_edge_preview_x = float(wx)
        model.dragging_edge_preview_y = float(wy)
        return True

    # Finalize or cancel on mouse up
    if et == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 1 and (
        getattr(model, 'dragging_edge_index', None) is not None or getattr(model, 'dragging_edge_id', None) is not None
    ):
        try:
            ei_val = getattr(model, 'dragging_edge_index', None)
            ei_int = int(ei_val) if ei_val is not None else -1
        except Exception:
            ei_int = -1
        eid = getattr(model, 'dragging_edge_id', None)
        end = getattr(model, 'dragging_edge_end', None)
        wx, wy = to_world(model, local_x, local_y)
        node = pick_node_world(model, float(wx), float(wy))
        changed = False
        edges = getattr(model, 'edges', []) or []
        # Resolve current index via ID if necessary
        try:
            if not isinstance(eid, str) or not eid:
                if len(getattr(model, 'edge_id_by_index', []) or []) != len(getattr(model, 'edges', []) or []):
                    model.rebuild_caches()
                if isinstance(ei_int, int) and 0 <= ei_int < len(getattr(model, 'edge_id_by_index', []) or []):
                    eid = model.edge_id_by_index[ei_int]
                else:
                    eid = None
            if isinstance(eid, str):
                if len(getattr(model, 'edge_index_by_id', {}) or {}) != len(getattr(model, 'edge_id_by_index', []) or []):
                    model.rebuild_caches()
                ei_now = getattr(model, 'edge_index_by_id', {}).get(eid)
            else:
                ei_now = ei_int if isinstance(ei_int, int) else None
        except Exception:
            ei_now = ei_int if isinstance(ei_int, int) else None
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
        # Clear drag state
        model.dragging_edge_index = None
        model.dragging_edge_id = None
        model.dragging_edge_end = None
        model.dragging_edge_preview_x = None
        model.dragging_edge_preview_y = None
        model.dragging_edge_orig_from = None
        model.dragging_edge_orig_to = None
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

    return False
