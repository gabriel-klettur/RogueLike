import pygame


def handle_toolbar_and_panels(owner, ev, camera, entities) -> bool:
    """Delegate mouse events to toolbar, add/remove, and tutorial panels in order.

    Returns True if any panel consumed the event.
    """
    if ev.type not in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL):
        return False

    try:
        toolbar = getattr(owner, "buildings_toolbar_controller", None)
        if toolbar and toolbar.handle_event(ev):
            return True
    except Exception:
        pass

    try:
        add_remove = getattr(owner, "add_remove", None)
        if add_remove and add_remove.is_active() and add_remove.handle_event(ev, camera, entities):
            return True
    except Exception:
        pass

    try:
        tutorial = getattr(owner, "tutorial", None)
        if tutorial and tutorial.is_active() and tutorial.handle_event(ev):
            return True
    except Exception:
        pass

    return False
