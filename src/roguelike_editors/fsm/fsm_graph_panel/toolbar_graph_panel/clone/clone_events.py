from __future__ import annotations
import logging

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.clone")


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


class CloneEventHandler:
    def on_select(self, controller, model, view) -> None:
        LOGGER.debug("[Clone] selected")

    def on_deselect(self, controller, model, view) -> None:
        LOGGER.debug("[Clone] deselected")

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
        src = _pick_node(model, wx, wy)
        if src is None:
            return True
        # Build clone
        try:
            from roguelike_editors.fsm.services.fsm_id import new_id
        except Exception:
            def new_id(prefix: str, existing: set[str]) -> str:
                i = 1
                while f"{prefix}_{i}" in existing:
                    i += 1
                return f"{prefix}_{i}"
        existing = {n.get('id') for n in getattr(model, 'nodes', []) if isinstance(n.get('id'), str)}
        nid = new_id('state', set(existing))
        # Offsets can be injected by the tool controller via panel view
        try:
            dx = int(getattr(view, 'clone_offset_dx', 20))
            dy = int(getattr(view, 'clone_offset_dy', 20))
        except Exception:
            dx, dy = 20, 20
        node = {
            'id': nid,
            'label': src.get('label', ''),
            'x': int(src.get('x', 0)) + dx,
            'y': int(src.get('y', 0)) + dy,
            'w': int(src.get('w', 120)),
            'h': int(src.get('h', 60)),
        }
        getattr(model, 'nodes', []).append(node)
        # Optional: select the clone
        try:
            model.selected_node_id = nid
            model.selected_edge_index = None
            model.selected_edge_id = None
        except Exception:
            pass
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
        LOGGER.debug("[Clone] cloned node %s -> %s", src.get('id'), nid)
        return True


__all__ = ["CloneEventHandler"]
