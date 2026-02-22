from __future__ import annotations
import logging
from ...services import persist_layout, persist_sets_structural

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.connect")


def _to_world(model, lx: int, ly: int):
    z = max(0.05, float(getattr(model, 'zoom', 1.0)))
    return ((lx - float(getattr(model, 'pan_x', 0.0))) / z, (ly - float(getattr(model, 'pan_y', 0.0))) / z)


def _pick_node(model, wx: float, wy: float):
    nodes = getattr(model, 'nodes', []) or []
    for n in reversed(list(nodes)):
        nx = int(n.get('x', 0)); ny = int(n.get('y', 0))
        nw = int(n.get('w', 120)); nh = int(n.get('h', 60))
        if nx <= wx <= nx + nw and ny <= wy <= ny + nh:
            return n
    return None


class ConnectEventHandler:
    def on_select(self, controller, model, view) -> None:
        LOGGER.debug("[Connect] selected")

    def on_deselect(self, controller, model, view) -> None:
        # Clear pending source if any when leaving the tool
        if getattr(model, 'connect_source_node_id', None):
            model.connect_source_node_id = None
        LOGGER.debug("[Connect] deselected")

    def handle_event(self, controller, event, *, model, view, canvas_rect) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        if canvas_rect is None:
            return False
        if getattr(event, 'type', None) != pygame.MOUSEBUTTONDOWN or getattr(event, 'button', None) != 1:
            return False
        mouse = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        if not canvas_rect.collidepoint(mouse):
            return False
        lx = mouse[0] - canvas_rect.left
        ly = mouse[1] - canvas_rect.top
        wx, wy = _to_world(model, lx, ly)
        node = _pick_node(model, wx, wy)
        if node is None:
            return True  # consume click in canvas to avoid select-mode actions
        nid = node.get('id')
        if not nid:
            return True
        src = getattr(model, 'connect_source_node_id', None)
        if not src:
            model.connect_source_node_id = nid
            LOGGER.debug("[Connect] source selected: %s", nid)
            return True
        if src == nid:
            # Same node clicked twice: cancel source selection
            model.connect_source_node_id = None
            LOGGER.debug("[Connect] canceled (same node)")
            return True
        # Create new edge from src to nid
        e = {'from': src, 'to': nid}
        getattr(model, 'edges', []).append(e)
        try:
            model.rebuild_caches()
        except Exception:
            pass
        try:
            persist_sets_structural(model)
        except Exception:
            pass
        try:
            persist_layout(model)
        except Exception:
            pass
        model.connect_source_node_id = None
        LOGGER.debug("[Connect] added edge %s -> %s", src, nid)
        return True


__all__ = ["ConnectEventHandler"]
