import pytest
from roguelike_editors.inventory.left_panel.tabs.tabs_model import TabsModel


def test_default_values():
    m = TabsModel()
    assert m.categories == ['player', 'monsters', 'map']
    assert m.current_category == 'player'


def test_independent_categories_list():
    m1 = TabsModel()
    m2 = TabsModel()
    m1.categories.append('new_category')
    assert 'new_category' not in m2.categories


def test_change_current_category_assignment():
    m = TabsModel()
    m.current_category = 'map'
    assert m.current_category == 'map'
