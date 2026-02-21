from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController
from .button.button_event_handler import ButtonEventHandler
from .input.input_event_handler import InputEventHandler
from .list.list_event_handler import ListEventHandler
from .tabs.tabs_event_handler import TabsEventHandler
from .tittle.tittle_event_handler import TittleEventHandler

class ItemSelectionPanelEventHandler:
    """
    Event handler para el panel de selección de ítems que coordina
    con el controlador de grid para agregar ítems.
    """
    def __init__(self, grid_controller, controller: ItemSelectionPanelController, view):
        self.grid_controller = grid_controller
        self.controller = controller
        self.view = view
        self.model = controller.model
        # Initialize specialized event handlers
        self.handlers = [
            InputEventHandler(self.controller, self.view),
            TittleEventHandler(self.controller, self.view),
            ListEventHandler(self.controller, self.view),
            TabsEventHandler(self.controller, self.view),
            ButtonEventHandler(self.grid_controller, self.controller, self.view),
        ]

    def handle(self, event):
        if not self.controller.model.show_panel:
            return False
        # Delegate to specialized handlers
        for handler in self.handlers:
            if handler.handle(event):
                return True
        return False
