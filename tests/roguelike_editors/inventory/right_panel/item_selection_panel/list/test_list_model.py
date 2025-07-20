import pytest
from roguelike_editors.inventory.right_panel.item_selection_panel.list.list_model import ListModel


def test_default_values():
    model = ListModel()
    assert model.visible_count == 10
    assert model.scroll_offset == 0
    assert model.selected_item is None
    assert model.selected_index is None


def test_property_assignment_and_independence():
    m1 = ListModel(5)
    m2 = ListModel(8)
    m1.scroll_offset = 3
    m1.selected_item = 'x'
    m1.selected_index = 1
    assert m2.scroll_offset == 0
    assert m2.visible_count == 8
    assert m1.visible_count == 5
    assert m2.selected_item is None
    assert m1.selected_item == 'x'
    assert m1.selected_index == 1
