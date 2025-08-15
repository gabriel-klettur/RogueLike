import os
import json
import logging
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config import DATA_DIR
from .clear_colliders_model import ClearCollidersModel
from .clear_colliders_events import ClearCollidersEvents
from .clear_colliders_view import ClearCollidersView

logger = logging.getLogger(__name__)


class ClearCollidersController:
    """
    Controller for the Clear Colliders tool (toolbar-integrated).

    Responsibilities:
    - Toggle clear_colliders mode (mutually exclusive with other tools)
    - Handle map clicks while in clear_colliders_mode to open confirmation dialog
    - Handle confirmation dialog clicks (delegate to events)
    - Finalize: persist cleared colliders for a zone and request map reload
    """

    def __init__(self, *, editor_state, map_controller=None, toolbar_controller=None):
        self.editor = editor_state
        self.map_controller = map_controller  # MapEditorController
        self.toolbar = toolbar_controller     # MapToolBarPanelController

        self.model = ClearCollidersModel(editor_state)
        self.events = ClearCollidersEvents(self, self.model)
        self.view = ClearCollidersView(self, self.model)

    # ---- API used by toolbar panel/events ----
    def toggle(self) -> bool:
        """Toggle clear_colliders mode and enforce exclusivity with other modes."""
        return self.model.toggle_mode()

    # ---- API used by map editor events ----
    def handle_map_click(self, tx: int, ty: int) -> bool:
        """
        When in clear_colliders_mode and user clicks the map:
        - detect zone under cursor (skip sentinels)
        - open confirmation dialog for that zone
        - turn off clear_colliders_mode
        """
        if not getattr(self.editor, "clear_colliders_mode", False):
            return False
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if zn in ("no zone", "no-zone"):
                continue
            w, h = global_map_settings.zone_size
            if ox <= tx < ox + w and oy <= ty < oy + h:
                self.model.begin_confirmation(zn)
                self.model.disable_mode()
                logger.debug(f"[Toolbar/ClearCollidersController] pending clear for zone={zn}")
                return True
        return False

    def finalize(self, zone: str) -> bool:
        """
        Persist a zone-sized collisions grid with all '.' (walkable) and reload map.
        SpatialIndex rebuild is handled by the event handler after calling this.
        """
        w, h = global_map_settings.zone_size
        grid = [["." for _ in range(w)] for _ in range(h)]
        path = os.path.join(DATA_DIR, "collisions", f"{zone}.json")
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w", encoding="utf-8") as f:
                json.dump(grid, f, indent=2)
            logger.debug(f"[Toolbar/ClearCollidersController] cleared colliders for zone={zone}")
        except Exception as e:
            logger.debug(f"[Toolbar/ClearCollidersController] failed to clear colliders for zone={zone}: {e}")
            return False
        try:
            if self.map_controller is not None:
                self.map_controller.map_manager.reload_map()
        except Exception:
            # Do not fail the tool on reload errors
            pass
        return True
