import pygame


def handle_wheel(owner, editor, controller, ev, camera, buildings) -> None:
    """Recompute overlapped buildings under cursor and cycle selection.

    Mirrors legacy _handle_mouse_wheel behavior and keeps tutorial guard.
    """
    hovered_list = list(getattr(editor, "hovered_buildings", []) or [])
    if not hovered_list:
        mx, my = pygame.mouse.get_pos()
        hovered_list = controller._buildings_under_mouse((mx, my), camera, buildings)
        editor.hovered_buildings = hovered_list
        if not hovered_list:
            return

    cur = getattr(editor, "hovered_building", None)
    try:
        base_idx = hovered_list.index(cur) if cur in hovered_list else editor.hovered_building_index
    except Exception:
        base_idx = 0

    delta = -1 if getattr(ev, "y", 0) < 0 else 1
    idx = (base_idx + delta) % len(hovered_list)

    editor.hovered_building_index = idx
    editor.hovered_building = hovered_list[idx]

    tutorial_active = False
    try:
        t = getattr(owner, "tutorial", None)
        tutorial_active = bool(t and t.is_active())
    except Exception:
        tutorial_active = False

    if (not tutorial_active) and getattr(editor, "current_tool", "select") == "select" and not getattr(editor, "colliders_mode", False):
        editor.active_building = hovered_list[idx]
