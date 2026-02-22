import logging

logger = logging.getLogger(__name__)


class PaintCollidersEvents:
    """
    Events for the Paint Colliders tool.
    - Handles confirmation dialog clicks.

    Español: Maneja los clics del diálogo de confirmación para pintar colliders.
    """

    def __init__(self, controller, model):
        self.controller = controller  # PaintCollidersController
        self.model = model

    def handle_confirm_click(self, mouse_pos) -> bool:
        e = self.controller.editor
        if not getattr(e, "confirm_paint_colliders", False):
            return False
        yes_r = getattr(e, "confirm_paint_colliders_yes_rect", None)
        no_r = getattr(e, "confirm_paint_colliders_no_rect", None)
        if yes_r and yes_r.collidepoint(mouse_pos):
            zone = getattr(e, "pending_paint_colliders_zone", None)
            if zone:
                tiles = (
                    self.controller.map_controller.map_manager.tiles_by_zone.get(zone, [])
                    if self.controller.map_controller
                    else []
                )
                e.begin_async_tool("paint_colliders", zone, tiles)
            self.model.reset_dialog()
            logger.debug("[Toolbar/PaintCollidersEvents] confirm paint YES")
            return True
        if no_r and no_r.collidepoint(mouse_pos):
            self.model.reset_dialog()
            logger.debug("[Toolbar/PaintCollidersEvents] confirm paint NO")
            return True
        return False
