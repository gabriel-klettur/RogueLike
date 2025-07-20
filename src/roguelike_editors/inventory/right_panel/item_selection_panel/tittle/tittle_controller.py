from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

class TittleController:
    """
    Controller for panel open/close logic.
    """
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model

    def open(self, default_items: list[str], ground_items: list[str]):
        """
        Initialize panel with default and ground items and display it.
        """
        self.model.default_items = default_items
        self.model.ground_items = ground_items
        self.model.current_tab = 'default'
        # Legacy available_items for compatibility
        self.model.available_items = default_items
        self.model.scroll_offset = 0
        self.model.selected_item = None
        self.model.quantity = 1
        self.model.selected_index = None
        self.model.show_panel = True

    def close(self):
        """
        Hide the item selection panel.
        """
        self.model.show_panel = False
