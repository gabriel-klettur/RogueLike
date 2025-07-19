from roguelike_editors.inventory.events.right_panel.inventory_items_panel.buttons.add_item.add_item_event_handler import AddItemEventHandler
from roguelike_editors.inventory.events.right_panel.inventory_items_panel.buttons.delete.delete_event_handler import DeleteEventHandler
from roguelike_editors.inventory.events.right_panel.inventory_items_panel.buttons.save.save_event_handler import SaveEventHandler
from roguelike_editors.inventory.events.right_panel.inventory_items_panel.grid.grid_event_handler import GridEventHandler
from roguelike_editors.inventory.events.right_panel.inventory_items_panel.tabs.tabs_event_handler import TabsEventHandler


class InventoryItemsPanelEventHandler:
    """
    Facade event handler que delega en sub-handlers:
    add_item, delete, save, grid y tabs.
    """
    def __init__(self, grid_controller):
        self.grid_controller = grid_controller
        # Sub-event handlers especializados
        self.handlers = [
            AddItemEventHandler(grid_controller),
            DeleteEventHandler(grid_controller),
            SaveEventHandler(grid_controller),
            GridEventHandler(grid_controller),
            TabsEventHandler(grid_controller),
        ]

    def handle(self, event):
        """
        Itera por los sub-handlers y retorna True si alguno consumió el evento.
        """
        for handler in self.handlers:
            try:
                if handler.handle(event):
                    return True
            except Exception:
                pass
        return False
