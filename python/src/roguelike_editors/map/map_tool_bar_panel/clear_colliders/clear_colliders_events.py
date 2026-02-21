import logging

logger = logging.getLogger(__name__)


class ClearCollidersEvents:
    """
    Events for the Clear Colliders tool.
    - Handles confirmation dialog clicks.
    """

    def __init__(self, controller, model):
        self.controller = controller  # ClearCollidersController
        self.model = model

    def handle_confirm_click(self, mouse_pos) -> bool:
        e = self.controller.editor
        if not getattr(e, "confirm_clear_colliders", False):
            return False
        yes_r = getattr(e, "confirm_clear_colliders_yes_rect", None)
        no_r = getattr(e, "confirm_clear_colliders_no_rect", None)
        if yes_r and yes_r.collidepoint(mouse_pos):
            zone = getattr(e, "pending_clear_colliders_zone", None)
            if zone:
                tiles = self.controller.map_controller.map_manager.tiles_by_zone.get(zone, []) if self.controller.map_controller else []
                e.begin_async_tool("clear_colliders", zone, tiles)
            self.model.reset_dialog()
            logger.debug("[Toolbar/ClearCollidersEvents] confirm clear YES")
            return True
        if no_r and no_r.collidepoint(mouse_pos):
            self.model.reset_dialog()
            logger.debug("[Toolbar/ClearCollidersEvents] confirm clear NO")
            return True
        return False
