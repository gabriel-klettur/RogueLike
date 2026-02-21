import pygame
from typing import Callable


def handle_mousedown(owner, editor, controller, ev, camera, entities) -> None:
    mx, my = pygame.mouse.get_pos()
    if getattr(ev, "button", None) == 1 and not getattr(editor, "colliders_mode", False):
        ab = getattr(editor, "active_building", None)
        if ab is not None and getattr(ab, "id", None) is None:
            try:
                dv = getattr(controller, "default_view", None)
                get_rect = getattr(dv, "get_delete_handle_rect", None) if dv else None
                if callable(get_rect):
                    delete_rect = get_rect(ab, camera)
                    if delete_rect and delete_rect.collidepoint(mx, my):
                        controller._delete_building(ab, entities.buildings)
                        return
            except Exception:
                pass
    controller.on_mouse_down((mx, my), getattr(ev, "button", 0), camera, entities.buildings)


def handle_mouseup(owner, controller, ev, camera, entities, save_fn: Callable, state) -> None:
    controller.on_mouse_up(getattr(ev, "button", 0), camera, entities.buildings)
    try:
        save_fn(
            entities.buildings,
            z_state=state.z_state,
            zone_offsets=getattr(owner, "zone_offsets", None),
        )
    except Exception:
        pass


def handle_motion(editor, controller, ev, camera, entities, is_blocked_fn: Callable[[int, int], bool]) -> bool:
    mx, my = getattr(ev, "pos", (0, 0))
    try:
        if is_blocked_fn(mx, my):
            editor.hovered_buildings = []
            editor.hovered_building = None
            return True
    except Exception:
        pass
    controller.on_mouse_motion((mx, my), camera, entities.buildings)
    return False
