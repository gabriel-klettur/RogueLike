from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

class InputController:
    """
    Controller for quantity input logic.
    """
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model

    def set_quantity(self, value: str):
        """
        Parse and set quantity from string input.
        """
        try:
            qty = int(value)
        except (ValueError, TypeError):
            qty = 1
        self.model.quantity = qty
