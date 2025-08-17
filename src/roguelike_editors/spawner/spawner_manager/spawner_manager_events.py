from __future__ import annotations


class SpawnerManagerEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        if not controller.model.visible:
            return False
        # Delegate to list panel first
        rect = getattr(getattr(controller.list_controller, 'view', None), 'panel_rect', None)
        handled = False
        if rect is not None:
            handled = controller.list_controller.handle_event(event)
            if handled:
                return True
            et = getattr(event, 'type', None)
            pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
            if et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                if rect.collidepoint(pos):
                    return True
        # Keyboard: R to refresh from disk
        if getattr(event, 'type', None) == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_r:
            controller.list_controller.refresh_from_disk()
            return True
        return False


__all__ = ["SpawnerManagerEventHandler"]
