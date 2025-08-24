import logging

logger = logging.getLogger(__name__)


class DeleteZoneEvents:
    """
    Events for the Delete Zone tool.
    - Handles confirmation dialog clicks for deleting a zone.
    """

    def __init__(self, controller, model):
        self.controller = controller  # DeleteZoneController
        self.model = model

    def handle_confirm_click(self, mouse_pos) -> bool:
        e = self.controller.editor
        if not getattr(e, "confirm_delete_zone", False):
            return False
        yes_r = getattr(e, "confirm_yes_rect", None)
        no_r = getattr(e, "confirm_no_rect", None)
        if yes_r and yes_r.collidepoint(mouse_pos):
            zone = getattr(e, "pending_delete_zone", None)
            if zone:
                # Select target and invoke map controller delete flow
                self.controller.editor.selected_zone = zone
                if self.controller.map_controller is not None:
                    self.controller.map_controller.delete_zone()
            self.model.reset_dialog()
            logger.debug("[Toolbar/DeleteZoneEvents] confirm delete YES")
            return True
        if no_r and no_r.collidepoint(mouse_pos):
            self.model.reset_dialog()
            logger.debug("[Toolbar/DeleteZoneEvents] confirm delete NO")
            return True
        return False
