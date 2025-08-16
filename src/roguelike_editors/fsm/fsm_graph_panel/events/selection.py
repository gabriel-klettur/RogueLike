from __future__ import annotations

from typing import Any
from ..services.hit_test import pick_node_world
from ..model import to_world


def handle_selection_event(controller: Any, model: Any, view: Any, event: Any) -> bool:
    """Handle selection and node drag for the 'select' tool.
    - LMB down: select node under cursor and start drag with offset
    - Mouse move: move dragged node
    - LMB up: stop drag
    Returns True if consumed.
    """
    try:
        import pygame  # type: ignore
    except Exception:
        return False

    if getattr(model, 'active_graph_tool', 'select') != 'select':
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

    # LMB down: select and begin drag if on node
    if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1 and inside:
        wx, wy = to_world(model, local_x, local_y)
        node = pick_node_world(model, wx, wy)
        if node is not None:
            nid = node.get('id')
            model.selected_node_id = nid
            model.dragging_node_id = nid
            model.drag_offset_x = node.get('x', 0) - wx
            model.drag_offset_y = node.get('y', 0) - wy
            return True
        # Click empty space: deselect node
        model.selected_node_id = None
        return True

    # Mouse move: move node while dragging
    if et == pygame.MOUSEMOTION and getattr(model, 'dragging_node_id', None):
        wx, wy = to_world(model, local_x, local_y)
        nid = model.dragging_node_id
        for n in getattr(model, 'nodes', []) or []:
            if n.get('id') == nid:
                n['x'] = int(wx + float(getattr(model, 'drag_offset_x', 0.0)))
                n['y'] = int(wy + float(getattr(model, 'drag_offset_y', 0.0)))
                break
        return True

    # LMB up: stop dragging
    if et == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 1:
        if getattr(model, 'dragging_node_id', None):
            model.dragging_node_id = None
            return True
        # Also consume plain clicks in select tool to avoid propagation
        if inside:
            return True

    return False
