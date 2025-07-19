from roguelike_editors.inventory.model.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

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
        self.model.selected_index = None
        self.model.show_panel = True

    def close(self):
        self.model.show_panel = False

    def select_item(self, item: str):
        self.model.selected_item = item

    def confirm(self):
        item_str = self.model.selected_item
        # cantidad deseada del input
        qty = self.model.quantity
        # Ground tab: extraer base y cap al tamaño del stack
        if self.model.current_tab == 'ground' and isinstance(item_str, str) and ' x' in item_str:
            base, orig_str = item_str.rsplit(' x', 1)
            try:
                orig_qty = int(orig_str)
            except (ValueError, IndexError):
                orig_qty = qty
            # default: si qty==1 tomar toda la pila, sino capear al tamaño del stack
            if qty == 1:
                qty = orig_qty
            else:
                qty = min(qty, orig_qty)
            item = base
        else:
            item = item_str
        # Ground tab: restar la cantidad del ground_items
        if self.model.current_tab == 'ground' and self.model.selected_index is not None:
            idx = self.model.selected_index
            orig_entry = self.model.ground_items[idx]
            base, orig_str = orig_entry.rsplit(' x', 1)
            try:
                orig_qty = int(orig_str)
            except (ValueError, IndexError):
                orig_qty = qty
            remaining = orig_qty - qty
            if remaining > 0:
                self.model.ground_items[idx] = f"{base} x{remaining}"
            else:
                self.model.ground_items.pop(idx)
            self.model.selected_index = None
        # Cerrar panel y retornar
        self.close()
        return item, qty
