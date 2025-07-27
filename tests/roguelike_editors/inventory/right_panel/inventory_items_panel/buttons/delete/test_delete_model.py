import pytest
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_model import DeleteModel


def test_default_values():
    m = DeleteModel()
    assert not m.show_delete_mode
    assert not m.show_delete_quantity_input
    assert m.delete_quantity == 1


def test_independent_instances():
    m1 = DeleteModel()
    m2 = DeleteModel()
    m1.show_delete_mode = True
    assert not m2.show_delete_mode


def test_property_assignment():
    m = DeleteModel()
    m.show_delete_mode = True
    m.show_delete_quantity_input = True
    m.delete_quantity = 5
    assert m.show_delete_mode
    assert m.show_delete_quantity_input
    assert m.delete_quantity == 5
