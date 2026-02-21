import logging

logger = logging.getLogger(__name__)


class AddZoneEvents:
    """
    Events for the Add Zone tool.
    - Handles confirmation dialog clicks for adding a zone.
    """

    def __init__(self, controller, model):
        self.controller = controller  # AddZoneController
        self.model = model

    def handle_confirm_click(self, mouse_pos) -> bool:
        e = self.controller.editor
        if not getattr(e, "confirm_add_zone", False):
            return False
        yes_r = getattr(e, "confirm_add_yes_rect", None)
        no_r = getattr(e, "confirm_add_no_rect", None)
        if yes_r and yes_r.collidepoint(mouse_pos):
            coords = getattr(e, "pending_add_zone_coords", None)
            if coords and self.controller.map_controller is not None:
                tx, ty = coords
                self.controller.map_controller.add_zone(tx, ty)
            self.model.reset_dialog()
            logger.debug("[Toolbar/AddZoneEvents] confirm add YES")
            return True
        if no_r and no_r.collidepoint(mouse_pos):
            self.model.reset_dialog()
            logger.debug("[Toolbar/AddZoneEvents] confirm add NO")
            return True
        return False
