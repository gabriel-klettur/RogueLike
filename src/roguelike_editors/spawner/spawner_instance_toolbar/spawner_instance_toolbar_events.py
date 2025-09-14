from __future__ import annotations

import logging
logger = logging.getLogger(__name__)


class SpawnerInstanceToolbarEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except ImportError:
            return False

        toolbar = getattr(controller.view, 'toolbar', None)
        if toolbar is None:
            return False

        # Panel rect for hit-testing
        try:
            panel_pos = toolbar.panel.pos or (toolbar.x, toolbar.y)
            panel_rect = pygame.Rect(panel_pos, toolbar.panel.surface.get_size())
        except (AttributeError, TypeError, ValueError):
            return False
        # Optional dropdown rect
        dropdown_rect = getattr(controller.view, 'dropdown_rect', None)

        # Block wheel over toolbar
        if getattr(event, 'type', None) == pygame.MOUSEWHEEL:
            mouse_pos = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mouse_pos) or (dropdown_rect and dropdown_rect.collidepoint(mouse_pos)):
                return True

        # Handle LMB clicks on icons
        if getattr(event, 'type', None) == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = getattr(event, 'pos', None)
            if not pos:
                return False
            # Dropdown selection first
            if dropdown_rect and dropdown_rect.collidepoint(pos):
                # Iterate item rects to find selection
                items = getattr(controller.view, 'dropdown_item_rects', []) or []
                for tpl_id, r in items:
                    try:
                        if r.collidepoint(pos):
                            try:
                                controller.on_add_template_selected(str(tpl_id))
                            except (AttributeError, TypeError, ValueError):
                                logger.debug("[InstanceToolbar] on_add_template_selected failed", exc_info=False)
                            return True
                    except (AttributeError, TypeError, ValueError):
                        continue
                # Clicked dropdown background -> consume
                return True
            # Outside-click cancels Add Mode if dropdown open
            if dropdown_rect and not (panel_rect.collidepoint(pos) or dropdown_rect.collidepoint(pos)):
                try:
                    controller.model.add_mode_active = False
                    controller.model.add_templates = []
                    controller.editor_controller.model.add_mode_active = False
                    world = getattr(getattr(controller.editor_controller, 'game', None), 'ecs', None)
                    world = getattr(world, 'ecs_world', None)
                    if world and hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except AttributeError:
                    pass
                return True
            # If click is not on panel, ignore
            if not panel_rect.collidepoint(pos):
                return False
            icon_rects = getattr(toolbar, 'icon_rects', {})
            # Add
            rect = icon_rects.get('add_spawner')
            if rect and rect.collidepoint(pos):
                try:
                    controller.on_add_spawner()
                except AttributeError:
                    logger.debug("[InstanceToolbar] on_add_spawner failed", exc_info=False)
                return True
            # Remove
            rect = icon_rects.get('remove_spawner')
            if rect and rect.collidepoint(pos):
                try:
                    controller.on_remove_spawner()
                except AttributeError:
                    logger.debug("[InstanceToolbar] on_remove_spawner failed", exc_info=False)
                return True
            # Clicked toolbar background: block
            return True

        # Consume other clicks inside panel (except RMB for drag handled by DraggablePanel, and MMB for panning)
        if getattr(event, 'type', None) in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            pos = getattr(event, 'pos', None)
            if pos and panel_rect.collidepoint(pos):
                if getattr(event, 'button', None) in (2, 3):
                    return False
                return True
            # Consume clicks inside dropdown
            if dropdown_rect and pos and dropdown_rect.collidepoint(pos):
                if getattr(event, 'button', None) in (2, 3):
                    return False
                return True

        return False


__all__ = ["SpawnerInstanceToolbarEventHandler"]
