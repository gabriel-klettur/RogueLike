from roguelike_editors.inventory.model.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

class TabsController:
    """
    Controller for tab switching logic.
    """
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model

    def change_tab(self, tab: str):
        """
        Switch current tab and reset list and quantity.
        """
        if tab not in ('default', 'ground'):
            return
        self.model.current_tab = tab
        # Legacy available_items compatibility
        if tab == 'default':
            self.model.available_items = self.model.default_items
        else:
            self.model.available_items = self.model.ground_items
        self.model.scroll_offset = 0
        # reset selection and quantity
        self.model.quantity = 1
        self.model.selected_item = None
        self.model.selected_index = None
