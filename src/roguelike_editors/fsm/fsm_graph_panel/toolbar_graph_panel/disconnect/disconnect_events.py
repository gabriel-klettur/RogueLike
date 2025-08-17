from __future__ import annotations
import logging
from ...services import persist_layout, persist_sets_structural

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.disconnect")


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


class DisconnectEventHandler:
    def on_select(self, controller, model, view) -> None:
        LOGGER.debug("[Disconnect] selected")

    def on_deselect(self, controller, model, view) -> None:
        if getattr(model, 'connect_source_node_id', None):
            model.connect_source_node_id = None
        LOGGER.debug("[Disconnect] deselected")

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
            return True
        nid = node.get('id')
        if not nid:
            return True
        src = getattr(model, 'connect_source_node_id', None)
        if not src:
            model.connect_source_node_id = nid
            LOGGER.debug("[Disconnect] source chosen: %s", nid)
            return True
        if src == nid:
            model.connect_source_node_id = None
            LOGGER.debug("[Disconnect] canceled (same node)")
            return True
        # Remove all edges from src->nid
        before = len(getattr(model, 'edges', []) or [])
        try:
            model.edges = [e for e in (model.edges or []) if not (e.get('from') == src and e.get('to') == nid)]
        except Exception:
            pass
        after = len(getattr(model, 'edges', []) or [])
        removed = before - after
        try:
            model.rebuild_caches()
        except Exception:
            pass
        if removed > 0:
            try:
                persist_sets_structural(model)
            except Exception:
                pass
            try:
                persist_layout(model)
            except Exception:
                pass
            LOGGER.debug("[Disconnect] removed %d edge(s) %s -> %s", removed, src, nid)
        else:
            LOGGER.debug("[Disconnect] no edges found %s -> %s", src, nid)
        model.connect_source_node_id = None
        return True


__all__ = ["DisconnectEventHandler"]
