import pytest
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_controller import InventoryItemsPanelController

@pytest.fixture
def setup_controller():
    # stub editor_controller
    model = SimpleNamespace(items_panel_model='panel_model')
    world = 'world_obj'
    editor_controller = SimpleNamespace(model=model, world=world)
    ctrl = InventoryItemsPanelController(editor_controller)
    return ctrl


def test_subcontrollers_created(setup_controller):
    ctrl = setup_controller
    # verify subcontrollers exist
    assert hasattr(ctrl, 'add_controller')
    assert hasattr(ctrl, 'delete_controller')
    assert hasattr(ctrl, 'save_controller')
    assert hasattr(ctrl, 'grid_controller')
    assert hasattr(ctrl, 'tabs_controller')


def test_delegation_methods(setup_controller):
    ctrl = setup_controller
    # stub subcontrollers methods
    ctrl.add_controller = SimpleNamespace(load_available_items=lambda: 'avail', start_add_item=lambda: 'start', select_item=lambda x: f'sel_{x}', confirm_quantity=lambda q: f'conf_{q}')
    ctrl.delete_controller = SimpleNamespace(delete_item=lambda idx, qty=None: (idx, qty))
    ctrl.save_controller = SimpleNamespace(save_default=lambda: 'def', save_active=lambda: 'act')

    assert ctrl.load_available_items() == 'avail'
    assert ctrl.start_add_item() == 'start'
    assert ctrl.select_item('i1') == 'sel_i1'
    assert ctrl.confirm_quantity(7) == 'conf_7'
    assert ctrl.delete_item(3, 2) == (3, 2)
    assert ctrl.save_default() == 'def'
    assert ctrl.save_active() == 'act'
    # alias compatibility
    assert ctrl._save_default() == 'def'
    assert ctrl._save_active() == 'act'
