from roguelike_editors.inventory.model.right_panel.item_selection_panel_model import ItemSelectionPanelModel

class ItemSelectionPanelController:
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model

    def open(self, default_items: list[str], ground_items: list[str]):
        # Set default and ground items
        self.model.default_items = default_items
        self.model.ground_items = ground_items
        self.model.current_tab = 'default'
        # Legacy available_items for compatibility
        self.model.available_items = default_items
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
        # Si es ground tab, parsear item y cantidad del string "item xqty"
        if getattr(self.model, 'current_tab', None) == 'ground' and isinstance(item, str) and ' x' in item:
            parts = item.rsplit(' x', 1)
            item = parts[0]
            try:
                qty = int(parts[1])
            except ValueError:
                pass
        self.close()
        return item, qty
