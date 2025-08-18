from __future__ import annotations

import logging
logger = logging.getLogger(__name__)


class SpawnerInstanceToolbarEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False

        toolbar = getattr(controller.view, 'toolbar', None)
        if toolbar is None:
            return False

        # Panel rect for hit-testing
        try:
            panel_pos = toolbar.panel.pos or (toolbar.x, toolbar.y)
            panel_rect = pygame.Rect(panel_pos, toolbar.panel.surface.get_size())
        except Exception:
            return False

        # Block wheel over toolbar
        if getattr(event, 'type', None) == pygame.MOUSEWHEEL:
            mouse_pos = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mouse_pos):
                return True

        # Handle LMB clicks on icons
        if getattr(event, 'type', None) == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = getattr(event, 'pos', None)
            if not pos or not panel_rect.collidepoint(pos):
                return False
            icon_rects = getattr(toolbar, 'icon_rects', {})
            # Add
            rect = icon_rects.get('add_spawner')
            if rect and rect.collidepoint(pos):
                try:
                    controller.on_add_spawner()
                except Exception:
                    logger.debug("[InstanceToolbar] on_add_spawner failed", exc_info=False)
                return True
            # Remove
            rect = icon_rects.get('remove_spawner')
            if rect and rect.collidepoint(pos):
                try:
                    controller.on_remove_spawner()
                except Exception:
                    logger.debug("[InstanceToolbar] on_remove_spawner failed", exc_info=False)
                return True
            # Clicked toolbar background: block
            return True

        # Consume other clicks inside panel (except RMB for drag handled by DraggablePanel)
        if getattr(event, 'type', None) in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            pos = getattr(event, 'pos', None)
            if pos and panel_rect.collidepoint(pos):
                if getattr(event, 'button', None) == 3:
                    return False
                return True

        return False


__all__ = ["SpawnerInstanceToolbarEventHandler"]
