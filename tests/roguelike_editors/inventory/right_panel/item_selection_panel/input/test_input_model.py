import pytest
from roguelike_editors.inventory.right_panel.item_selection_panel.input.input_model import InputModel

def test_default_quantity():
    model = InputModel()
    assert model.quantity == 1

@pytest.mark.parametrize("qty", [0, 5, 10])
def test_custom_quantity(qty):
    model = InputModel(qty)
    assert model.quantity == qty
