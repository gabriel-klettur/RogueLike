from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

class ListController:
    """
    Controller for item list selection logic.
    """
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model

    def select_item(self, item: str, index: int | None = None):
        """
        Select an item from list; record item and optional index for ground tab.
        """
        self.model.selected_item = item
        if self.model.current_tab == 'ground':
            self.model.selected_index = index
        else:
            self.model.selected_index = None

    def reset_selection(self):
        """
        Reset selected item and index.
        """
        self.model.selected_item = None
        self.model.selected_index = None
