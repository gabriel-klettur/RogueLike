import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_controller import DeleteController

@pytest.fixture
def setup_controller():
    # Setup editor controller with model and world
    default_data = {'player': {'slots': [{'item': 'A', 'quantity': 5}, None]}}
    active_data = {'player': {'e1': {'slots': [{'item': 'A', 'quantity': 3}, None]}}}
    inv_comp = SimpleNamespace(slots=[SimpleNamespace(item_id='A', quantity=4), None])
    world = SimpleNamespace(components={'InventoryComponent': {1: inv_comp}})
    editor_model = SimpleNamespace(default_data=default_data, active_data=active_data, selected_eid='e1', current_category='player', editing_side='default')
    editor_controller = SimpleNamespace(model=editor_model, world=world)
    parent = SimpleNamespace(model=SimpleNamespace(delete=None))
    # parent.model.delete not used in default case
    ctrl = DeleteController(editor_controller, parent)
    return ctrl, editor_model, default_data, active_data, world


def test_delete_in_default_player_partial(setup_controller):
    ctrl, editor_model, default_data, *_ = setup_controller
    parent_model_delete = SimpleNamespace(delete_quantity=2)
    ctrl.model = parent_model_delete
    ctrl.delete_item(slot_idx=0)
    assert default_data['player']['slots'][0]['quantity'] == 3


def test_delete_in_default_player_full(setup_controller):
    ctrl, editor_model, default_data, *_ = setup_controller
    parent_model_delete = SimpleNamespace(delete_quantity=5)
    ctrl.model = parent_model_delete
    ctrl.delete_item(slot_idx=0)
    assert default_data['player']['slots'][0] is None


def test_delete_in_active_player_and_ecs(setup_controller):
    ctrl, editor_model, default_data, active_data, world = setup_controller
    editor_model.editing_side = 'active'
    # numeric eid key used is int('e1') -> ValueError; adjust model
    editor_model.selected_eid = 1
    editor_model.current_category = 'player'
    # Test ECS slot removal
    ctrl.model.delete_quantity = world.components['InventoryComponent'][1].slots[0].quantity
    ctrl.delete_item(slot_idx=0)
    inv_comp = world.components['InventoryComponent'][1]
    assert inv_comp.slots[0] is None
