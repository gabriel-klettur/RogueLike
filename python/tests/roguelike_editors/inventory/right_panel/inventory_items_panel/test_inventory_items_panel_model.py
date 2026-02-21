import pytest
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_model import InventoryitemsPanelModel
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.add_item.add_item_model import AddItemModel
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_model import DeleteModel
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.save.save_model import SaveModel
from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_model import GridModel
from roguelike_editors.inventory.right_panel.inventory_items_panel.tabs.tabs_model import TabsModel


def test_submodels_types_and_defaults():
    m = InventoryitemsPanelModel()
    assert isinstance(m.add_item, AddItemModel)
    assert isinstance(m.delete, DeleteModel)
    assert isinstance(m.save, SaveModel)
    assert isinstance(m.grid, GridModel)
    assert isinstance(m.tabs, TabsModel)


def test_property_wrappers():
    m = InventoryitemsPanelModel()
    # add_item wrappers
    m.available_items = ['a', 'b']
    assert m.add_item.available_items == ['a', 'b']
    assert m.available_items == ['a', 'b']
    m.show_item_list = True
    assert m.add_item.show_item_list is True
    m.selected_item = 'item1'
    assert m.add_item.selected_item == 'item1'
    m.show_quantity_input = True
    assert m.add_item.show_quantity_input is True
    m.quantity = 5
    assert m.add_item.quantity == 5
    # delete wrappers
    m.show_delete_mode = True
    assert m.delete.show_delete_mode is True
    m.show_delete_quantity_input = True
    assert m.delete.show_delete_quantity_input is True
    m.delete_quantity = 2
    assert m.delete.delete_quantity == 2
    # grid wrapper
    assert m.grid_model is m.grid


def test_independent_instances():
    m1 = InventoryitemsPanelModel()
    m2 = InventoryitemsPanelModel()
    m1.available_items = ['x']
    assert m2.available_items != ['x']
    m1.delete_quantity = 9
    assert m2.delete_quantity != 9
    assert m1.grid_model is not m2.grid_model
