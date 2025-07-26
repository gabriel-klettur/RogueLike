import pytest
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.add_item.add_item_model import AddItemModel


def test_default_values():
    m = AddItemModel()
    assert m.available_items == []
    assert not m.show_item_list
    assert m.selected_item is None
    assert not m.show_quantity_input
    assert m.quantity == 1


def test_independent_available_items():
    m1 = AddItemModel()
    m2 = AddItemModel()
    m1.available_items.append('item1')
    assert 'item1' not in m2.available_items


def test_property_assignment():
    m = AddItemModel()
    m.show_item_list = True
    m.selected_item = 'x'
    m.show_quantity_input = True
    m.quantity = 5
    assert m.show_item_list
    assert m.selected_item == 'x'
    assert m.show_quantity_input
    assert m.quantity == 5
