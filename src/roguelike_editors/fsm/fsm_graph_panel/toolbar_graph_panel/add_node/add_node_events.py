from __future__ import annotations
import logging
from typing import Any
from ...services import persist_layout, persist_sets_structural

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.add_node")


class AddNodeEventHandler:
    def on_select(self, controller, model, view) -> None:
        LOGGER.debug("[AddNode] selected")

    def on_deselect(self, controller, model, view) -> None:
        LOGGER.debug("[AddNode] deselected")

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
        # Convert to local and world coords
        lx = mouse[0] - canvas_rect.left
        ly = mouse[1] - canvas_rect.top
        z = max(0.05, float(getattr(model, 'zoom', 1.0)))
        wx = (lx - float(getattr(model, 'pan_x', 0.0))) / z
        wy = (ly - float(getattr(model, 'pan_y', 0.0))) / z
        # Build new node
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
        node = {
            'id': nid,
            'label': '',
            'x': int(wx),
            'y': int(wy),
            'w': 120,
            'h': 60,
        }
        getattr(model, 'nodes', []).append(node)
        try:
            model.rebuild_caches()
        except Exception:
            pass
        # Persist structural + layout
        try:
            persist_sets_structural(model)
        except Exception:
            pass
        try:
            persist_layout(model)
        except Exception:
            pass
        LOGGER.debug("[AddNode] added node id=%s at world=(%d,%d)", nid, node['x'], node['y'])
        return True


__all__ = ["AddNodeEventHandler"]
