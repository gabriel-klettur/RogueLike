from roguelike_editors.inventory.model.item_selection_panel_model import ItemSelectionPanelModel

class ItemSelectionPanelController:
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model

    def open(self, items: list[str]):
        self.model.available_items = items
        self.model.scroll_offset = 0
        self.model.selected_item = None
        self.model.quantity = 1
        self.model.show_panel = True

    def close(self):
        self.model.show_panel = False

    def select_item(self, item: str):
        self.model.selected_item = item

    def confirm(self):
        item = self.model.selected_item
        qty = self.model.quantity
        self.close()
        return item, qty
