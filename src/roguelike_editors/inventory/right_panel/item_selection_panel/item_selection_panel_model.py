import pygame
from .input.input_model import InputModel
from .list.list_model import ListModel
from .tabs.tabs_model import TabsModel
from .tittle.tittle_model import TittleModel
from .button.button_model import ButtonModel

class ItemSelectionPanelModel:
    def __init__(self, available_items: list[str] = None, visible_count: int = 10):
        items = available_items or []
        self.tabs_model = TabsModel(items)
        self.list_model = ListModel(visible_count)
        self.input_model = InputModel()
        self.tittle_model = TittleModel()
        self.button_model = ButtonModel()

    # Tabs properties
    @property
    def available_items(self) -> list[str]:
        return self.tabs_model.available_items

    @available_items.setter
    def available_items(self, items: list[str]):
        self.tabs_model.available_items = items

    @property
    def default_items(self) -> list[str]:
        return self.tabs_model.default_items

    @default_items.setter
    def default_items(self, items: list[str]):
        self.tabs_model.default_items = items

    @property
    def ground_items(self) -> list[str]:
        return self.tabs_model.ground_items

    @ground_items.setter
    def ground_items(self, items: list[str]):
        self.tabs_model.ground_items = items

    @property
    def current_tab(self) -> str:
        return self.tabs_model.current_tab

    @current_tab.setter
    def current_tab(self, tab: str):
        self.tabs_model.current_tab = tab

    # List properties
    @property
    def visible_count(self) -> int:
        return self.list_model.visible_count

    @visible_count.setter
    def visible_count(self, count: int):
        self.list_model.visible_count = count

    @property
    def scroll_offset(self) -> int:
        return self.list_model.scroll_offset

    @scroll_offset.setter
    def scroll_offset(self, offset: int):
        self.list_model.scroll_offset = offset

    @property
    def selected_item(self) -> str | None:
        return self.list_model.selected_item

    @selected_item.setter
    def selected_item(self, item: str | None):
        self.list_model.selected_item = item

    @property
    def selected_index(self) -> int | None:
        return self.list_model.selected_index

    @selected_index.setter
    def selected_index(self, index: int | None):
        self.list_model.selected_index = index

    # Input properties
    @property
    def quantity(self) -> int:
        return self.input_model.quantity

    @quantity.setter
    def quantity(self, qty: int):
        self.input_model.quantity = qty

    # Tittle properties
    @property
    def show_panel(self) -> bool:
        return self.tittle_model.show_panel

    @show_panel.setter
    def show_panel(self, show: bool):
        self.tittle_model.show_panel = show

    # Button (drag) properties
    @property
    def drag_offset(self) -> pygame.Vector2:
        return self.button_model.drag_offset

    @drag_offset.setter
    def drag_offset(self, offset: pygame.Vector2):
        self.button_model.drag_offset = offset

    @property
    def dragging(self) -> bool:
        return self.button_model.dragging

    @dragging.setter
    def dragging(self, dragging: bool):
        self.button_model.dragging = dragging

    @property
    def drag_start_pos(self) -> pygame.Vector2:
        return self.button_model.drag_start_pos

    @drag_start_pos.setter
    def drag_start_pos(self, pos: pygame.Vector2):
        self.button_model.drag_start_pos = pos
