import pytest
from roguelike_editors.inventory.right_panel.inventory_items_panel.tabs.tabs_model import TabsModel


def test_default_values():
    m = TabsModel()
    assert m.active_tab == 'default'
    assert m.available_tabs == ['default', 'active']


def test_independent_available_tabs():
    m1 = TabsModel()
    m2 = TabsModel()
    m1.available_tabs.append('custom')
    assert m2.available_tabs == ['default', 'active']


def test_property_assignment():
    m = TabsModel()
    m.active_tab = 'active'
    m.available_tabs = ['one']
    assert m.active_tab == 'active'
    assert m.available_tabs == ['one']
