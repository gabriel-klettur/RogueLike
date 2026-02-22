import logging

from roguelike_engine.config.map_config import global_map_settings

from .add_zone_model import AddZoneModel
from .add_zone_events import AddZoneEvents
from .add_zone_view import AddZoneView

logger = logging.getLogger(__name__)


class AddZoneController:
    """
    Controller for the Add Zone tool (toolbar-integrated).

    Responsibilities:
    - Toggle add_zone mode (mutually exclusive with other tools)
    - Handle map clicks while in add_zone_mode to open confirmation dialog
    - Handle confirmation dialog clicks (delegate to events)
    - Optionally provide a small view hook (unused now; dialog is drawn by MapEditorView)
    """

    def __init__(self, *, editor_state, map_controller=None, toolbar_controller=None):
        self.editor = editor_state
        self.map_controller = map_controller  # MapEditorController
        self.toolbar = toolbar_controller     # MapToolBarPanelController

        self.model = AddZoneModel(editor_state)
        self.events = AddZoneEvents(self, self.model)
        self.view = AddZoneView(self, self.model)

    # ---- API used by toolbar panel/events ----
    def toggle(self) -> bool:
        """Toggle add_zone mode and enforce exclusivity with other modes."""
        new_val = self.model.toggle_mode()

        # Special-case: blank world (no zones defined). When the user selects
        # Add Zone mode in this context, immediately propose creating a zone
        # at the origin (0,0) without requiring a map click.
        if new_val:
            try:
                is_blank = getattr(global_map_settings, "is_blank_world", None)
                if callable(is_blank) and global_map_settings.is_blank_world():
                    # Use tile (0,0) as the initial placement and open the
                    # confirmation dialog, mirroring the normal click flow.
                    self.model.begin_placement(0, 0)
                    # Disable mode after staging the dialog, same as when a
                    # click on the map has been processed.
                    self.model.disable_mode()
                    new_val = False
            except Exception:
                # Never break toolbar toggle due to diagnostics/blank-world checks
                pass

        return new_val

    # ---- API used by map editor events ----
    def handle_map_click(self, tx: int, ty: int) -> bool:
        """
        When in add_zone_mode and user clicks the map:
        - record grid coords
        - open confirmation dialog
        - turn off add_zone_mode
        """
        if not getattr(self.editor, "add_zone_mode", False):
            return False
        self.model.begin_placement(tx, ty)
        self.model.disable_mode()
        return True
