from __future__ import annotations
import logging

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_sets_panel.clone.events")


class SetsPanelCloneEventHandler:
    def handle_button_click(self, parent_controller, index: int) -> bool:
        """Handle click on the clone button for the given row index.
        Delegates to the clone controller and always consumes the event.
        """
        try:
            parent_controller.clone.clone_by_index(parent_controller, index)
        except Exception as ex:
            LOGGER.exception("[SetsPanelClone] button click failed for index=%s: %s", index, ex)
        return True


__all__ = ["SetsPanelCloneEventHandler"]
