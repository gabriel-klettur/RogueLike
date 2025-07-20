import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.left_panel.list.list_controller import ListController


def editor_controller_fixture():
    model = SimpleNamespace(
        active_data={},
        default_data={},
        selected_eid=None,
    )
    world = SimpleNamespace(components={})
    ec = SimpleNamespace(model=model, world=world)
    return ec


def test_select_entity():
    ec = editor_controller_fixture()
    panel_model = SimpleNamespace(selected_eid=None)
    lc = ListController(ec, panel_model)
    lc.select_entity('ent1')
    assert panel_model.selected_eid == 'ent1'
    assert ec.model.selected_eid == 'ent1'


def test_get_items_list_player():
    ec = editor_controller_fixture()
    ec.model.active_data = {
        'player': {
            'e1': {'slots': [{'item': 'Sword', 'quantity': 2}, None]}
        }
    }
    panel_model = SimpleNamespace(current_category='player')
    lc = ListController(ec, panel_model)
    items = lc.get_items_list()
    assert items == ['Sword x2']


def test_get_items_list_monsters_empty():
    ec = editor_controller_fixture()
    ec.model.active_data = {'monsters': {}}
    ec.world.components = {}
    panel_model = SimpleNamespace(current_category='monsters')
    lc = ListController(ec, panel_model)
    items = lc.get_items_list()
    assert items == []


def test_get_items_list_other():
    ec = editor_controller_fixture()
    ec.model.active_data = {
        'map': {
            'i1': {'item_id': 'Gold', 'quantity': 3, 'position': {'x': 1, 'y': 2}}
        }
    }
    panel_model = SimpleNamespace(current_category='map')
    lc = ListController(ec, panel_model)
    items = lc.get_items_list()
    assert items == ['Gold x3 @(1.0,2.0)']
