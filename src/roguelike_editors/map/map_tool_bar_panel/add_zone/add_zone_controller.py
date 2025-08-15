import logging
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
        return self.model.toggle_mode()

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
