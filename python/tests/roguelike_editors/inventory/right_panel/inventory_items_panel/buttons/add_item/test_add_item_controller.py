import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.add_item.add_item_model import AddItemModel
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.add_item.add_item_controller import AddItemController
import pygame

@pytest.fixture
def make_controller(monkeypatch):
    # Parent controller with AddItemModel
    model = AddItemModel()
    parent = SimpleNamespace(model=model)
    # Editor controller with view and model
    items = {'I1': None, 'I2': None}
    view = SimpleNamespace(items=items, item_panel_controller=SimpleNamespace(open=lambda a, b: None))
    editor_model = SimpleNamespace(active_data={'map': {'k1': {'item_id': 'X', 'quantity': 2}}},
                                   default_data={'player': {'slots': [{'item': 'I1', 'quantity': 1}, None]}},
                                   selected_eid='e1', current_category='player', editing_side=None)
    world = SimpleNamespace(components={})
    editor_controller = SimpleNamespace(view=view, model=editor_model, world=world)
    ctrl = AddItemController(editor_controller, parent)
    return ctrl, model, editor_controller, parent


def test_load_available_items(make_controller):
    ctrl, model, *_ = make_controller
    ctrl.load_available_items()
    assert model.available_items == ['I1', 'I2']


def test_start_add_item(monkeypatch, make_controller):
    ctrl, model, editor_controller, _ = make_controller
    record = {}
    # stub open
    editor_controller.view.item_panel_controller.open = lambda default, ground: record.update({'default': default, 'ground': ground})
    editor_controller.model.editing_side = 'active'
    ctrl.start_add_item()
    assert model.show_item_list
    assert not model.show_quantity_input
    assert model.selected_item is None
    assert model.quantity == 1
    assert record['default'] == ['I1', 'I2']
    assert record['ground'] == ['X x2']


def test_select_item(make_controller):
    ctrl, model, *_ = make_controller
    ctrl.select_item('I2')
    assert model.selected_item == 'I2'
    assert model.show_quantity_input


def test_confirm_quantity_default_player_existing(make_controller):
    ctrl, model, editor_controller, _ = make_controller
    # default side
    editor_controller.model.editing_side = 'default'
    editor_controller.model.current_category = 'player'
    ctrl.model.selected_item = 'I1'
    ctrl.confirm_quantity(3)
    slots = editor_controller.model.default_data['player']['slots']
    assert slots[0]['quantity'] == 4
    assert not model.show_item_list and not model.show_quantity_input and model.quantity == 1 and model.selected_item is None


def test_confirm_quantity_default_player_new_slot(make_controller):
    ctrl, model, editor_controller, _ = make_controller
    # reset default_data to one empty slot
    editor_controller.model.default_data['player']['slots'] = [None]
    editor_controller.model.editing_side = 'default'
    editor_controller.model.current_category = 'player'
    ctrl.model.selected_item = 'NEW'
    ctrl.confirm_quantity(5)
    slots = editor_controller.model.default_data['player']['slots']
    assert any(s and s.get('item') == 'NEW' and s.get('quantity') == 5 for s in slots)
