import pytest
from roguelike_editors.inventory.right_panel.item_selection_panel.tabs.tabs_model import TabsModel


def test_default_empty():
    model = TabsModel()
    assert model.default_items == []
    assert model.ground_items == []
    assert model.current_tab == 'default'
    assert model.available_items == []


def test_initial_with_items():
    items = ['a', 'b']
    model = TabsModel(items)
    assert model.default_items == items
    assert model.ground_items == []
    assert model.available_items == items


def test_default_items_independent_of_available():
    items = ['x', 'y']
    model = TabsModel(items)
    items.append('z')
    # default_items is a copy, should not include 'z'
    assert model.default_items == ['x', 'y']
    # available_items references the items list, should include 'z'
    assert model.available_items == ['x', 'y', 'z']
