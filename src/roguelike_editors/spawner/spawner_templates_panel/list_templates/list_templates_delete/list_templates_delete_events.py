from __future__ import annotations
import logging

LOGGER = logging.getLogger("roguelike_editors.spawner.spawner_templates_panel.list_templates.delete.events")


class ListTemplatesDeleteEventHandler:
    def handle_button_click(self, parent_controller, index: int) -> bool:
        """Handle click on the delete button for the given row index.
        Opens the confirmation modal via delete controller.
        """
        try:
            parent_controller.delete.ask_confirm_delete(parent_controller, index)
        except Exception as ex:
            LOGGER.exception("[SpawnerTemplatesDelete] button click failed for index=%s: %s", index, ex)
        return True

    def handle_modal_event(self, parent_controller, event) -> bool:
        """Handle events while the confirmation modal is visible.
        Consumes interactions: left click on Sí/No and keys (Enter/Y, Esc/N).
        """
        try:
            import pygame  # type: ignore
        except Exception:
            # If pygame unavailable, just consume to avoid propagation
            return True
        dview = parent_controller.delete_view
        rect = getattr(parent_controller.view, 'panel_rect', None) or getattr(parent_controller.view, 'panel_rect', None)
        if not rect:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                yes_r = getattr(dview, 'confirm_yes_rect', None)
                no_r = getattr(dview, 'confirm_no_rect', None)
                if yes_r is not None and yes_r.collidepoint(pos):
                    parent_controller.delete.confirm_yes(parent_controller)
                    return True
                if no_r is not None and no_r.collidepoint(pos):
                    parent_controller.delete.confirm_no(parent_controller)
                    return True
                # Consume other clicks over panel while modal shown
                return True
        if et == pygame.KEYDOWN:
            key = getattr(event, 'key', None)
            if key in (pygame.K_RETURN, pygame.K_y):
                parent_controller.delete.confirm_yes(parent_controller)
                return True
            if key in (pygame.K_ESCAPE, pygame.K_n):
                parent_controller.delete.confirm_no(parent_controller)
                return True
        # Block everything else while modal
        if rect.collidepoint(pos):
            return True
        return False


__all__ = ["ListTemplatesDeleteEventHandler"]
