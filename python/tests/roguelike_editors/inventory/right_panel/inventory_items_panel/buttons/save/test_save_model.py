import pytest
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.save.save_model import SaveModel


def test_default_values():
    m = SaveModel()
    assert not m.save_in_progress
    assert m.save_message == ""


def test_independent_instances():
    m1 = SaveModel()
    m2 = SaveModel()
    m1.save_in_progress = True
    m1.save_message = "Test"
    assert not m2.save_in_progress
    assert m2.save_message == ""


def test_property_assignment():
    m = SaveModel()
    m.save_in_progress = True
    m.save_message = "Guardado exitoso"
    assert m.save_in_progress
    assert m.save_message == "Guardado exitoso"
