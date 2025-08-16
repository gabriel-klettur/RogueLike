from __future__ import annotations
import logging
from .services.hit_test import (
    pick_node_world,
)
from .events.hover import update_hover_state
from .events.navigation import handle_navigation_event
from .events.text_edit import begin_text_edit, begin_edge_text_edit, handle_text_input_event
from .events.selection import handle_selection_event
from .events.edges import handle_edge_drag_event
from .model import to_world as model_to_world, begin_pan, update_pan, end_pan


class FsmGraphPanelEventHandler:
    def handle_event(self, controller, event) -> bool:
        # Delegates key parts of event handling from the controller.
        # Initial scope: toolbar events, inline text editing, ESC cancel, toolbar clicks, legend toggle.
        try:
            import pygame  # type: ignore
        except Exception:
            return False

        model = getattr(controller, 'model', None)
        view = getattr(controller, 'view', None)
        if not getattr(model, 'visible', False):
            return False

        rect = getattr(view, 'canvas_rect', None)
        if rect is None:
            return False

        et = getattr(event, 'type', None)
        mouse_pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        btn = getattr(event, 'button', None)
        inside = rect.collidepoint(mouse_pos)
        local_x = mouse_pos[0] - rect.left
        local_y = mouse_pos[1] - rect.top
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
        zoom = float(getattr(model, 'zoom', 1.0))

        # Helpers are provided by model (to_world) and services.hit_test

        # Delegate mouse wheel and other toolbar-handled events to toolbar events
        try:
            if et == pygame.MOUSEWHEEL:
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][WHEEL] delegating to toolbar_events. mouse=%s rect=%s", mouse_pos, rect
                )
            if getattr(controller, 'toolbar_events', None) and controller.toolbar_events.handle_event(
                event, canvas_rect=rect, graph_model=model
            ):
                # Persist viewport (zoom/pan) after toolbar-handled zoom
                try:
                    controller._persist_layout()
                except Exception:
                    pass
                return True
        except Exception:
            pass

        # Inline text input routing
        try:
            if handle_text_input_event(controller, model, view, event):
                return True
        except Exception:
            pass

        # Global ESC handling when not inline-editing: cancel drags or pending connect/disconnect
        try:
            if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
                # Cancel edge handle drag
                if getattr(model, 'dragging_edge_index', None) is not None or getattr(model, 'dragging_edge_id', None) is not None:
                    model.dragging_edge_index = None
                    model.dragging_edge_id = None
                    model.dragging_edge_end = None
                    model.dragging_edge_preview_x = None
                    model.dragging_edge_preview_y = None
                    model.dragging_edge_orig_from = None
                    model.dragging_edge_orig_to = None
                    model.hover_edge_handle_end = None
                    return True
                # Cancel pending connect/disconnect source selection
                tool = getattr(model, 'active_graph_tool', 'select')
                if tool in ('connect', 'disconnect') and getattr(model, 'connect_source_node_id', None):
                    model.connect_source_node_id = None
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                    return True
        except Exception:
            pass

        # Handle clicks on the graph toolbar buttons via toolbar controller
        if et == pygame.MOUSEBUTTONDOWN and btn == 1:
            try:
                if getattr(controller, 'toolbar', None) and controller.toolbar.handle_mouse_down(mouse_pos, rect, model):
                    # Persist viewport/tool state after toolbar interaction (e.g., zoom)
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                    # Activate tool runtime (if any) after changing active_graph_tool
                    try:
                        if hasattr(controller, '_activate_tool'):
                            controller._activate_tool(getattr(model, 'active_graph_tool', 'select'))
                    except Exception:
                        pass
                    return True
            except Exception:
                pass

        # Route navigation (pan/zoom) and selection via submodules
        try:
            if handle_navigation_event(controller, model, view, event):
                # Persist viewport at the end of pan or after zoom; rely on caller for zoom persistence elsewhere
                try:
                    import pygame  # type: ignore
                    if getattr(event, 'type', None) == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 2:
                        controller._persist_layout()
                except Exception:
                    pass
                return True
        except Exception:
            pass

        # Edge handle drag workflow
        try:
            if handle_edge_drag_event(controller, model, view, event):
                return True
        except Exception:
            pass

        try:
            if handle_selection_event(controller, model, view, event):
                return True
        except Exception:
            pass

        # Legend minimize/expand toggle and click capture
        if et == pygame.MOUSEBUTTONDOWN and btn == 1:
            try:
                lbr = getattr(view, 'legend_button_rect', None)
                lrect = getattr(view, 'legend_rect', None)
                # Click on button toggles
                if lbr is not None and lbr.collidepoint(mouse_pos):
                    model.legend_collapsed = not bool(getattr(model, 'legend_collapsed', False))
                    try:
                        controller._persist_layout()
                    except Exception:
                        pass
                    return True
                # Click inside legend body: consume; expand if collapsed
                if lrect is not None and lrect.collidepoint(mouse_pos):
                    if bool(getattr(model, 'legend_collapsed', False)):
                        model.legend_collapsed = False
                        try:
                            controller._persist_layout()
                        except Exception:
                            pass
                    return True
            except Exception:
                pass

        # Active tool delegation (non-select): let the current tool handle events first
        try:
            tool_key = str(getattr(model, 'active_graph_tool', 'select') or 'select')
        except Exception:
            tool_key = 'select'
        if tool_key != 'select':
            try:
                if hasattr(controller, '_dispatch_active_tool_event') and controller._dispatch_active_tool_event(event):
                    return True
            except Exception:
                pass

        # Middle mouse pan start
        if et == pygame.MOUSEBUTTONDOWN and btn == 2:
            logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                "[GraphPanel][PAN START] inside=%s mouse=%s local=(%d,%d) pan=(%s,%s) zoom=%.3f",
                inside, mouse_pos, int(local_x), int(local_y),
                getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                float(getattr(model, 'zoom', 1.0)),
            )
            begin_pan(model, int(local_x), int(local_y))
            return True

        # Mouse button up handling: node drag end, edge drag finalize, pan end
        if et == pygame.MOUSEBUTTONUP:
            if btn == 2 and getattr(model, 'dragging_pan', False):
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][PAN END] mouse=%s local=(%d,%d) pan=(%s,%s)",
                    mouse_pos, int(local_x), int(local_y),
                    getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                )
                end_pan(model)
                try:
                    controller._persist_layout()
                except Exception:
                    pass
                return True

        # Mouse motion: start pan with mid, start node drag with left, move node/pan/edge preview, update hovers
        if et == pygame.MOUSEMOTION:
            try:
                try:
                    buttons = pygame.mouse.get_pressed(5)
                except TypeError:
                    buttons = pygame.mouse.get_pressed()
                mid_down_now = bool(buttons[1]) if buttons and len(buttons) > 1 else False
            except Exception:
                mid_down_now = False

            if mid_down_now and not getattr(model, 'dragging_pan', False) and inside:
                if getattr(model, 'dragging_node_id', None) is not None:
                    model.dragging_node_id = None
                begin_pan(model, int(local_x), int(local_y))
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][PAN START@MOTION] mouse=%s local=(%d,%d) pan=(%s,%s) zoom=%.3f",
                    mouse_pos, int(local_x), int(local_y),
                    getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                    float(getattr(model, 'zoom', 1.0)),
                )
                return True

            if getattr(model, 'dragging_pan', False):
                before = (getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0))
                dx, dy = update_pan(model, int(local_x), int(local_y))
                logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.events").debug(
                    "[GraphPanel][PAN MOVE] mouse=%s local=(%d,%d) dx=%d dy=%d pan %s -> (%s,%s)",
                    mouse_pos, int(local_x), int(local_y), dx, dy, before,
                    getattr(model, 'pan_x', 0.0), getattr(model, 'pan_y', 0.0),
                )
                return True

            # Hover tracking via submodule
            try:
                update_hover_state(controller, model, view, (int(local_x), int(local_y)))
            except Exception:
                pass

            return True

        # Wheel and button 4/5 zoom are already handled above via navigation handler

        # Double-click-to-edit labels
        if et == pygame.MOUSEBUTTONDOWN and btn == 1 and inside:
            try:
                clicks = getattr(event, 'clicks', 0)
            except Exception:
                clicks = 0
            if clicks and clicks >= 2:
                # Prefer edge label if an edge is hovered
                idx = getattr(model, 'hover_edge_index', None)
                if idx is not None:
                    try:
                        begin_edge_text_edit(controller, model, view, int(idx))
                        return True
                    except Exception:
                        pass
                # Otherwise, start node label editing if a node is under cursor
                try:
                    wx, wy = model_to_world(model, local_x, local_y)
                    node = pick_node_world(model, float(wx), float(wy))
                    if node is not None and node.get('id'):
                        begin_text_edit(controller, model, view, str(node.get('id')))
                        return True
                except Exception:
                    pass

        return False


__all__ = ["FsmGraphPanelEventHandler"]
