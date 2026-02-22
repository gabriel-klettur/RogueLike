import pytest
from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_model import GridModel


def test_default_values():
    m = GridModel()
    assert m.selected_slot == -1
    assert m.hover_slot == -1
    assert m.grid_rows == 5
    assert m.grid_cols == 5
    assert not m.show_delete_mode


def test_independent_instances():
    m1 = GridModel()
    m2 = GridModel()
    m1.selected_slot = 2
    assert m2.selected_slot == -1


def test_property_assignment():
    m = GridModel()
    m.selected_slot = 3
    m.hover_slot = 4
    m.grid_rows = 3
    m.grid_cols = 4
    m.show_delete_mode = True
    assert m.selected_slot == 3
    assert m.hover_slot == 4
    assert m.grid_rows == 3
    assert m.grid_cols == 4
    assert m.show_delete_mode
