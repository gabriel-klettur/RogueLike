import pytest
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel
from roguelike_editors.inventory.right_panel.item_selection_panel.tittle.tittle_controller import TittleController

@pytest.fixture
def setup_controller():
    model = ItemSelectionPanelModel(['a'], visible_count=5)
    ctrl = TittleController(model)
    return ctrl, model


def test_open_initializes_model_and_shows_panel(setup_controller):
    ctrl, model = setup_controller
    default = ['x', 'y']
    ground = ['g1', 'g2']
    ctrl.open(default, ground)
    assert model.default_items == default
    assert model.ground_items == ground
    assert model.current_tab == 'default'
    assert model.available_items == default
    assert model.scroll_offset == 0
    assert model.selected_item is None
    assert model.quantity == 1
    assert model.selected_index is None
    assert model.show_panel is True


def test_close_hides_panel(setup_controller):
    ctrl, model = setup_controller
    # simulate panel open
    model.show_panel = True
    ctrl.close()
    assert model.show_panel is False
