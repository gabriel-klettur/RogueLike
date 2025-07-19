from roguelike_editors.inventory.model.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel
from .button.button_controller import ButtonController
from .input.input_controller import InputController
from .list.list_controller import ListController
from .tabs.tabs_controller import TabsController
from .tittle.tittle_controller import TittleController

class ItemSelectionPanelController:
    def __init__(self, model: ItemSelectionPanelModel):
        self.model = model
        self.title_controller = TittleController(model)
        self.tabs_controller = TabsController(model)
        self.list_controller = ListController(model)
        self.input_controller = InputController(model)
        self.button_controller = ButtonController(model)

    def open(self, default_items: list[str], ground_items: list[str]):
        self.title_controller.open(default_items, ground_items)

    def close(self):
        self.title_controller.close()

    def select_item(self, item: str):
        self.list_controller.select_item(item)

    def change_tab(self, tab: str):
        self.tabs_controller.change_tab(tab)

    def set_quantity(self, value: str):
        self.input_controller.set_quantity(value)

    def confirm(self) -> tuple[str, int]:
        item, qty = self.button_controller.confirm()
        self.close()
        return item, qty
