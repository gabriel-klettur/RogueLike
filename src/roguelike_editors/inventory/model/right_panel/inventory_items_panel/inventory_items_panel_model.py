from roguelike_editors.inventory.model.right_panel.inventory_items_panel.buttons.add_item.add_item_model import AddItemModel
from roguelike_editors.inventory.model.right_panel.inventory_items_panel.buttons.delete.delete_model import DeleteModel
from roguelike_editors.inventory.model.right_panel.inventory_items_panel.buttons.save.save_model import SaveModel
from roguelike_editors.inventory.model.right_panel.inventory_items_panel.grid.grid_model import GridModel
from roguelike_editors.inventory.model.right_panel.inventory_items_panel.tabs.tabs_model import TabsModel

class InventoryitemsPanelModel:
    """
    Modelo principal que delega en submodelos especializados:
    add_item, delete, save, grid y tabs.
    """
    def __init__(self):
        # Submodelos especializados
        self.add_item = AddItemModel()
        self.delete = DeleteModel()
        self.save = SaveModel()
        self.grid = GridModel()
        self.tabs = TabsModel()
    
    # Propiedades de compatibilidad para acceso directo
    @property
    def available_items(self):
        return self.add_item.available_items
    
    @available_items.setter
    def available_items(self, value):
        self.add_item.available_items = value
    
    @property
    def show_item_list(self):
        return self.add_item.show_item_list
    
    @show_item_list.setter
    def show_item_list(self, value):
        self.add_item.show_item_list = value
    
    @property
    def selected_item(self):
        return self.add_item.selected_item
    
    @selected_item.setter
    def selected_item(self, value):
        self.add_item.selected_item = value
    
    @property
    def show_quantity_input(self):
        return self.add_item.show_quantity_input
    
    @show_quantity_input.setter
    def show_quantity_input(self, value):
        self.add_item.show_quantity_input = value
    
    @property
    def quantity(self):
        return self.add_item.quantity
    
    @quantity.setter
    def quantity(self, value):
        self.add_item.quantity = value
    
    @property
    def show_delete_mode(self):
        return self.delete.show_delete_mode
    
    @show_delete_mode.setter
    def show_delete_mode(self, value):
        self.delete.show_delete_mode = value
    
    @property
    def show_delete_quantity_input(self):
        return self.delete.show_delete_quantity_input
    
    @show_delete_quantity_input.setter
    def show_delete_quantity_input(self, value):
        self.delete.show_delete_quantity_input = value
    
    @property
    def delete_quantity(self):
        return self.delete.delete_quantity
    
    @delete_quantity.setter
    def delete_quantity(self, value):
        self.delete.delete_quantity = value
