from dataclasses import dataclass, field
from roguelike_editors.inventory.model.left_panel.tabs.tabs_model import TabsModel
from roguelike_editors.inventory.model.left_panel.list.list_model import ListModel


@dataclass
class InventoryPanelModel:
    """
    Modelo para la vista de listado de entidades (tabs + lista scroll).
    Délega la lógica en TabsModel y ListModel.
    """
    tabs_model: TabsModel = field(default_factory=TabsModel)
    list_model: ListModel = field(default_factory=ListModel)

    @property
    def categories(self):
        return self.tabs_model.categories

    @categories.setter
    def categories(self, value):
        self.tabs_model.categories = value

    @property
    def current_category(self):
        return self.tabs_model.current_category

    @current_category.setter
    def current_category(self, value):
        self.tabs_model.current_category = value

    @property
    def selected_eid(self):
        return self.list_model.selected_eid

    @selected_eid.setter
    def selected_eid(self, value):
        self.list_model.selected_eid = value
