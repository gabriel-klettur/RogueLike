import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.button.button_controller import ButtonController

@pytest.fixture
def setup_model():
    model = SimpleNamespace(selected_item=None, quantity=1, current_tab=None, selected_index=None, ground_items=[])
    return model

@pytest.fixture
def setup_controller(setup_model):
    return ButtonController(setup_model)


def test_confirm_non_ground(setup_controller):
    ctrl = setup_controller
    ctrl.model.selected_item = 'sword'
    ctrl.model.quantity = 3
    ctrl.model.current_tab = 'inventory'
    item, qty = ctrl.confirm()
    assert item == 'sword'
    assert qty == 3


def test_confirm_ground_qty1_removal(setup_controller):
    ctrl = setup_controller
    # prepare ground scenario
    ctrl.model.current_tab = 'ground'
    ctrl.model.selected_item = 'gem x5'
    ctrl.model.quantity = 1
    ctrl.model.selected_index = 0
    ctrl.model.ground_items = ['gem x5']
    item, qty = ctrl.confirm()
    assert item == 'gem'
    assert qty == 5
    assert ctrl.model.ground_items == []
    assert ctrl.model.selected_index is None
    assert ctrl.model.selected_item is None


def test_confirm_ground_qty_gt1_remaining(setup_controller):
    ctrl = setup_controller
    ctrl.model.current_tab = 'ground'
    ctrl.model.selected_item = 'gem x5'
    ctrl.model.quantity = 2
    ctrl.model.selected_index = 0
    ctrl.model.ground_items = ['gem x5']
    item, qty = ctrl.confirm()
    assert item == 'gem'
    assert qty == 2
    assert ctrl.model.ground_items == ['gem x3']
    assert ctrl.model.selected_item == 'gem x3'
    assert ctrl.model.selected_index == 0


def test_confirm_ground_invalid_quantity_string(setup_controller):
    ctrl = setup_controller
    ctrl.model.current_tab = 'ground'
    ctrl.model.selected_item = 'gem xfoo'
    ctrl.model.quantity = 1
    # no removal since selected_index None
    ctrl.model.selected_index = None
    ctrl.model.ground_items = ['gem xfoo']
    item, qty = ctrl.confirm()
    assert item == 'gem'
    # qty should default to model.quantity when conversion fails
    assert qty == 1
