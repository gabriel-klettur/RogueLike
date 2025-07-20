import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.input.input_controller import InputController

@pytest.fixture
def setup_controller():
    model = SimpleNamespace(quantity=2)
    controller = InputController(model)
    return controller, model

@pytest.mark.parametrize("value,expected", [
    ("3", 3),
    ("0", 0),
    ("-1", -1),
])
def test_set_quantity_valid(setup_controller, value, expected):
    controller, model = setup_controller
    controller.set_quantity(value)
    assert model.quantity == expected

@pytest.mark.parametrize("value", ["abc", "", None])
def test_set_quantity_invalid_defaults_to_one(setup_controller, value):
    controller, model = setup_controller
    controller.set_quantity(value)
    assert model.quantity == 1
