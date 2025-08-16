from __future__ import annotations

from typing import Any, Optional


def begin_text_edit(controller: Any, model: Any, view: Any, node_id: str) -> None:
    """Start inline text editing for a node label.
    Sets model.editing_node_id and model.editing_text, clears edge edit state,
    and asks the view to begin the inline text input.
    """
    if not node_id:
        return
    try:
        # Find current label
        text: str = ""
        for n in getattr(model, "nodes", []) or []:
            if n.get("id") == node_id:
                text = str(n.get("label", "") or "")
                break
        # Update model state
        model.editing_node_id = node_id
        model.editing_edge_index = None
        model.editing_edge_id = None
        model.editing_text = text
        # Ask the view to prepare text input
        if hasattr(view, "begin_text_edit"):
            view.begin_text_edit(text, select_all=True)
    except Exception:
        return


def handle_text_input_event(controller: Any, model: Any, view: Any, event: Any) -> bool:
    """Centralized handling while inline TextInput is active.
    - ESC cancels
    - Delegate events to the widget
    - Commit on deactivate or click outside
    Returns True if consumed.
    """
    try:
        import pygame  # type: ignore
    except Exception:
        return False

    ti = getattr(view, 'text_input', None)
    if ti is None or not getattr(ti, 'active', False):
        return False

    et = getattr(event, 'type', None)
    rect = getattr(view, 'canvas_rect', None)
    mouse_pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
    local_x = (mouse_pos[0] - rect.left) if rect else mouse_pos[0]
    local_y = (mouse_pos[1] - rect.top) if rect else mouse_pos[1]

    # ESC cancels
    if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
        try:
            ti.deactivate()
        except Exception:
            pass
        model.editing_node_id = None
        model.editing_edge_index = None
        model.editing_edge_id = None
        model.editing_text = None
        return True

    # Delegate to widget; for mouse-down, adjust to local canvas coords
    try:
        if et == pygame.MOUSEBUTTONDOWN:
            try:
                adj_event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {
                    'pos': (int(local_x), int(local_y)),
                    'button': getattr(event, 'button', None),
                })
            except Exception:
                adj_event = event
            handled = bool(ti.handle_event(adj_event))
        else:
            handled = bool(ti.handle_event(event))
    except Exception:
        handled = False
    if handled:
        # Live-sync for dynamic sizing
        try:
            model.editing_text = str(getattr(ti, 'text', '') or '')
        except Exception:
            pass
        # If deactivated (Enter), commit to model and persist
        if not getattr(ti, 'active', False):
            text = str(getattr(ti, 'text', '') or '')
            if getattr(model, 'editing_node_id', None):
                nid = model.editing_node_id
                for n in getattr(model, 'nodes', []) or []:
                    if n.get('id') == nid:
                        n['label'] = text
                        break
            elif getattr(model, 'editing_edge_index', None) is not None:
                try:
                    ei = int(model.editing_edge_index)  # type: ignore[arg-type]
                except Exception:
                    ei = -1
                edges = getattr(model, 'edges', []) or []
                if isinstance(ei, int) and 0 <= ei < len(edges):
                    edges[ei]['label'] = text
            model.editing_node_id = None
            model.editing_edge_index = None
            model.editing_edge_id = None
            model.editing_text = None
            try:
                controller._persist_sets_structural()
            except Exception:
                pass
            try:
                controller._persist_layout()
            except Exception:
                pass
        return True

    # Click outside input rectangle: commit and close
    if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
        abs_r = getattr(view, 'text_input_abs_rect', None)
        if abs_r is None or not abs_r.collidepoint(mouse_pos):
            try:
                ti.deactivate()
            except Exception:
                pass
            text = str(getattr(ti, 'text', '') or '')
            if getattr(model, 'editing_node_id', None):
                nid = model.editing_node_id
                for n in getattr(model, 'nodes', []) or []:
                    if n.get('id') == nid:
                        n['label'] = text
                        break
            elif getattr(model, 'editing_edge_index', None) is not None:
                try:
                    ei = int(model.editing_edge_index)  # type: ignore[arg-type]
                except Exception:
                    ei = -1
                edges = getattr(model, 'edges', []) or []
                if isinstance(ei, int) and 0 <= ei < len(edges):
                    edges[ei]['label'] = text
            model.editing_node_id = None
            model.editing_edge_index = None
            model.editing_edge_id = None
            model.editing_text = None
            try:
                controller._persist_sets_structural()
            except Exception:
                pass
            try:
                controller._persist_layout()
            except Exception:
                pass
            return True

    # Swallow other events while editing
    return True


def commit_text_edit(controller: Any, model: Any, view: Any) -> None:
    """Commit current inline text edit (no-op if view handles commit on deactivate).
    This function is provided for completeness if a manual commit is needed.
    """
    try:
        ti = getattr(view, "text_input", None)
        if ti is not None and getattr(ti, "active", False):
            # Deactivate triggers commit path in the centralized handler
            ti.deactivate()
    except Exception:
        return


def cancel_text_edit(controller: Any, model: Any, view: Any) -> None:
    """Cancel current inline text edit, clearing model state and deactivating widget."""
    try:
        ti = getattr(view, "text_input", None)
        if ti is not None:
            try:
                ti.deactivate()
            except Exception:
                pass
        model.editing_node_id = None
        model.editing_edge_index = None
        model.editing_edge_id = None
        model.editing_text = None
    except Exception:
        return


def begin_edge_text_edit(controller: Any, model: Any, view: Any, edge_index: int) -> None:
    """Start inline text editing for an edge label by index."""
    try:
        ei = int(edge_index)
    except Exception:
        return
    try:
        edges = getattr(model, "edges", []) or []
        if not (0 <= ei < len(edges)):
            return
        text = str(edges[ei].get("label", "") or "")
        model.editing_node_id = None
        model.editing_edge_index = ei
        # Store id if caches present
        try:
            ids = getattr(model, "edge_id_by_index", []) or []
            if 0 <= ei < len(ids):
                model.editing_edge_id = ids[ei]
            else:
                model.editing_edge_id = None
        except Exception:
            model.editing_edge_id = None
        model.editing_text = text
        if hasattr(view, "begin_text_edit"):
            view.begin_text_edit(text, select_all=True)
    except Exception:
        return
