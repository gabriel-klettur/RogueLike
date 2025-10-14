import pygame


def handle_confirm_delete(editor, controller, ev, entities) -> bool:
    if getattr(editor, "confirm_delete_visible", False):
        et = getattr(ev, "type", None)
        if et == pygame.MOUSEBUTTONDOWN and getattr(ev, "button", None) == 1:
            mx, my = getattr(ev, "pos", (0, 0))
            yesr = getattr(editor, "confirm_yes_rect", None)
            nor = getattr(editor, "confirm_no_rect", None)
            if yesr is not None and pygame.Rect(yesr).collidepoint(mx, my):
                controller.confirm_delete_yes(entities.buildings)
                return True
            if nor is not None and pygame.Rect(nor).collidepoint(mx, my):
                controller.confirm_delete_no()
                return True
            return True
        if et == pygame.KEYDOWN:
            key = getattr(ev, "key", None)
            if key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                controller.confirm_delete_yes(entities.buildings)
                return True
            if key == pygame.K_ESCAPE:
                controller.confirm_delete_no()
                return True
        return True
    return False
