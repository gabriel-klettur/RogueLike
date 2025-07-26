import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.list.list_controller import ListController

@pytest.fixture
def setup_model():
    return SimpleNamespace(current_tab=None, selected_item=None, selected_index=None)

@pytest.fixture
def setup_controller(setup_model):
    return ListController(setup_model)


def test_select_item_non_ground(setup_controller, setup_model):
    ctrl = setup_controller
    model = setup_model
    ctrl.select_item('itemA', index=5)
    assert model.selected_item == 'itemA'
    assert model.selected_index is None


def test_select_item_ground(setup_controller, setup_model):
    ctrl = setup_controller
    model = setup_model
    model.current_tab = 'ground'
    ctrl.select_item('itemB', index=2)
    assert model.selected_item == 'itemB'
    assert model.selected_index == 2


def test_reset_selection(setup_controller, setup_model):
    ctrl = setup_controller
    model = setup_model
    model.selected_item = 'x'
    model.selected_index = 3
    ctrl.reset_selection()
    assert model.selected_item is None
    assert model.selected_index is None
