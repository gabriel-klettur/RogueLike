from __future__ import annotations

from typing import Any, Tuple
from ..services.hit_test import (
    pick_node_world,
    pick_edge_local,
    pick_edge_id_local,
    pick_edge_handle_local,
)
from ..model import to_world


def update_hover_state(controller: Any, model: Any, view: Any, mouse_pos: Tuple[int, int]) -> None:
    """Update hover state based on local (canvas) mouse position.
    Sets model.hover_node_id, model.hover_edge_index, model.hover_edge_id,
    and model.hover_edge_handle_end.
    """
    try:
        lx, ly = int(mouse_pos[0]), int(mouse_pos[1])
    except Exception:
        return None
    # Node hover uses world coords
    try:
        wx, wy = to_world(model, lx, ly)
        n = pick_node_world(model, float(wx), float(wy))
        model.hover_node_id = n.get("id") if n is not None else None
    except Exception:
        model.hover_node_id = None
    # Edge and handle hover use local coords and cached view paths
    try:
        idx = pick_edge_local(view, lx, ly)
        model.hover_edge_index = idx
        model.hover_edge_id = pick_edge_id_local(model, view, lx, ly)
        model.hover_edge_handle_end = pick_edge_handle_local(view, idx, lx, ly)
    except Exception:
        model.hover_edge_index = None
        model.hover_edge_id = None
        model.hover_edge_handle_end = None
    return None
