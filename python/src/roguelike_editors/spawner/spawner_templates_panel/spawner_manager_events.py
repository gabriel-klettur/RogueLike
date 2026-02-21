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
        list_rect = getattr(getattr(controller.list_controller, 'view', None), 'panel_rect', None)
        handled = False
        if list_rect is not None:
            handled = controller.list_controller.handle_event(event)
            if handled:
                # After list changes (e.g., selection), sync properties
                try:
                    controller._sync_selection_to_props()
                except Exception:
                    pass
                return True
            et = getattr(event, 'type', None)
            pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
            if et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                if list_rect.collidepoint(pos):
                    # Ensure properties reflect latest selection hover/changes
                    try:
                        controller._sync_selection_to_props()
                    except Exception:
                        pass
                    return True
        # Route to properties panel
        props_rect = getattr(getattr(controller.props_controller, 'view', None), 'panel_rect', None)
        if props_rect is not None and getattr(controller.props_controller.model, 'visible', False):
            if controller.props_controller.handle_event(event):
                return True
            et = getattr(event, 'type', None)
            pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
            if et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                if props_rect.collidepoint(pos):
                    return True
        # Keyboard: R to refresh from disk
        if getattr(event, 'type', None) == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_r:
            controller.list_controller.refresh_from_disk()
            try:
                controller._sync_selection_to_props()
            except Exception:
                pass
            return True
        return False


__all__ = ["SpawnerManagerEventHandler"]
