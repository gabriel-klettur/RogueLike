import logging
from roguelike_engine.config.map_config import global_map_settings
from .delete_zone_model import DeleteZoneModel
from .delete_zone_events import DeleteZoneEvents
from .delete_zone_view import DeleteZoneView

logger = logging.getLogger(__name__)


class DeleteZoneController:
    """
    Controller for the Delete Zone tool (toolbar-integrated).

    Responsibilities:
    - Toggle delete_zone mode (mutually exclusive with other tools)
    - Handle map clicks while in delete_zone_mode to open confirmation dialog
    - Handle confirmation dialog clicks (delegate to events)
    - Optionally provide a small view hook (unused now; dialog is drawn by MapEditorView)
    """

    def __init__(self, *, editor_state, map_controller=None, toolbar_controller=None):
        self.editor = editor_state
        self.map_controller = map_controller  # MapEditorController
        self.toolbar = toolbar_controller     # MapToolBarPanelController

        self.model = DeleteZoneModel(editor_state)
        self.events = DeleteZoneEvents(self, self.model)
        self.view = DeleteZoneView(self, self.model)

    # ---- API used by toolbar panel/events ----
    def toggle(self) -> bool:
        """Toggle delete_zone mode and enforce exclusivity with other modes."""
        return self.model.toggle_mode()

    # ---- API used by map editor events ----
    def handle_map_click(self, tx: int, ty: int) -> bool:
        """
        When in delete_zone_mode and user clicks the map:
        - determine clicked zone by grid coords
        - if valid (not sentinel, not 'lobby'), open confirmation dialog
        - turn off delete_zone_mode
        """
        if not getattr(self.editor, "delete_zone_mode", False):
            return False

        # Find the zone at tile (tx, ty)
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if zn in ("no zone", "no-zone", "lobby"):
                continue
            w, h = global_map_settings.zone_size
            if ox <= tx < ox + w and oy <= ty < oy + h:
                self.model.begin_delete(zn)
                self.model.disable_mode()
                return True
        return False

    def request_delete_selected(self) -> bool:
        """
        Open the delete confirmation dialog for the currently selected zone.
        Returns True if a valid zone was queued for deletion.
        """
        sel = getattr(self.editor, "selected_zone", None)
        if not sel or sel in ("no zone", "no-zone", "lobby"):
            return False
        # Ensure the selected zone exists in current offsets
        if sel not in getattr(global_map_settings, "zone_offsets", {}):
            return False
        self.model.begin_delete(sel)
        return True
