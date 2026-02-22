from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

class ButtonController:
    """
    Controller for confirm button logic.
    """
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model

    def confirm(self) -> tuple[str, int]:
        """
        Handle confirm logic: extract item and quantity, update ground items.
        Returns (item, qty).
        """
        item_str = self.model.selected_item
        qty = self.model.quantity

        if self.model.current_tab == 'ground' and isinstance(item_str, str) and ' x' in item_str:
            base, orig_str = item_str.rsplit(' x', 1)
            try:
                orig_qty = int(orig_str)
            except (ValueError, IndexError):
                orig_qty = qty
            # default: if qty==1 take entire stack, else cap at stack size
            if qty == 1:
                qty = orig_qty
            else:
                qty = min(qty, orig_qty)
            item = base
        else:
            item = item_str

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
                # Update the item with remaining quantity
                self.model.ground_items[idx] = f"{base} x{remaining}"
                # Keep the item selected with updated text
                self.model.selected_item = f"{base} x{remaining}"
            else:
                # Remove the item completely
                self.model.ground_items.pop(idx)
                # Clear selection since item was removed
                self.model.selected_index = None
                self.model.selected_item = None

        return item, qty
