import pytest
from types import SimpleNamespace
import roguelike_editors.inventory.left_panel.tabs.tabs_controller as tabs_ctrl
from roguelike_editors.inventory.left_panel.tabs.tabs_controller import TabsController

@pytest.fixture
def editor_controller():
    model = SimpleNamespace(active_data={'monsters': {}}, current_category=None, editing_side=None, selected_eid=None)
    data_controller = SimpleNamespace(paths={'monsters': {'active': 'dummy_path'}})
    world = SimpleNamespace(player_entity='player1')
    inv_list_ctrl = SimpleNamespace(debug_printed=True)
    inventory_panel_controller = SimpleNamespace(list_controller=inv_list_ctrl)
    ec = SimpleNamespace(model=model, data_controller=data_controller, world=world, inventory_panel_controller=inventory_panel_controller)
    return ec


def test_change_to_player_category(editor_controller):
    panel_model = SimpleNamespace(current_category='monsters', selected_eid='old')
    tc = TabsController(editor_controller, panel_model)
    tc.change_category('player')
    assert panel_model.current_category == 'player'
    assert panel_model.selected_eid == 'player1'
    assert editor_controller.model.current_category == 'player'
    assert editor_controller.model.selected_eid == 'player1'
    assert editor_controller.model.editing_side == 'active'


def test_change_to_monsters_category(monkeypatch, editor_controller):
    # stub load_from_json
    loaded = {'m1': {}, 'm2': {}}
    monkeypatch.setattr(tabs_ctrl, 'load_from_json', lambda path: loaded)
    panel_model = SimpleNamespace(current_category='player', selected_eid='old')
    tc = TabsController(editor_controller, panel_model)
    tc.change_category('monsters')
    assert panel_model.current_category == 'monsters'
    assert editor_controller.model.active_data['monsters'] == loaded
    # first key selected
    first = next(iter(loaded.keys()))
    assert panel_model.selected_eid == first
    assert editor_controller.model.selected_eid == first
    assert editor_controller.inventory_panel_controller.list_controller.debug_printed is False
    assert editor_controller.model.editing_side == 'active'
