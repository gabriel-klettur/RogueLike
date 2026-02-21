import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.tabs.tabs_controller import TabsController

@pytest.fixture
def setup_model():
    return SimpleNamespace(
        default_items=['d1'],
        ground_items=['g1'],
        current_tab='default',
        available_items=['d1'],
        scroll_offset=5,
        quantity=9,
        selected_item='x',
        selected_index=1
    )

@pytest.fixture
def setup_controller(setup_model):
    return TabsController(setup_model)


def test_change_to_ground_resets_and_switches(setup_controller, setup_model):
    ctrl = setup_controller
    model = setup_model
    ctrl.change_tab('ground')
    assert model.current_tab == 'ground'
    assert model.available_items == model.ground_items
    assert model.scroll_offset == 0
    assert model.quantity == 1
    assert model.selected_item is None
    assert model.selected_index is None


def test_change_to_default_resets_and_switches(setup_controller, setup_model):
    ctrl = setup_controller
    model = setup_model
    # simulate previous ground state
    model.current_tab = 'ground'
    model.available_items = model.ground_items
    model.scroll_offset = 3
    model.quantity = 5
    model.selected_item = 'y'
    model.selected_index = 2

    ctrl.change_tab('default')
    assert model.current_tab == 'default'
    assert model.available_items == model.default_items
    assert model.scroll_offset == 0
    assert model.quantity == 1
    assert model.selected_item is None
    assert model.selected_index is None


def test_invalid_tab_no_change(setup_controller, setup_model):
    ctrl = setup_controller
    model = setup_model
    orig = (
        model.current_tab,
        list(model.available_items),
        model.scroll_offset,
        model.quantity,
        model.selected_item,
        model.selected_index
    )
    ctrl.change_tab('invalid')
    assert (
        model.current_tab,
        model.available_items,
        model.scroll_offset,
        model.quantity,
        model.selected_item,
        model.selected_index
    ) == orig
