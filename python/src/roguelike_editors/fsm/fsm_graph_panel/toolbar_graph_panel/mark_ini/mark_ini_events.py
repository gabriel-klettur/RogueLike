from __future__ import annotations
import logging
from ...services import persist_layout, persist_sets_structural

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.mark_ini")


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


class MarkIniEventHandler:
    def on_select(self, controller, model, view) -> None:
        LOGGER.debug("[MarkIni] selected")

    def on_deselect(self, controller, model, view) -> None:
        LOGGER.debug("[MarkIni] deselected")

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
        if not node:
            return True  # consume click with no effect
        nid = node.get('id')
        if not nid:
            return True
        # Set unique initial flag
        for n in getattr(model, 'nodes', []) or []:
            try:
                n['initial'] = (n.get('id') == nid)
            except Exception:
                continue
        try:
            persist_sets_structural(model)
        except Exception:
            pass
        try:
            persist_layout(model)
        except Exception:
            pass
        LOGGER.debug("[MarkIni] initial node set to %s", nid)
        return True


__all__ = ["MarkIniEventHandler"]
