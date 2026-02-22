import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_controller import GridController

@pytest.fixture
def setup_controller():
    # Stub save_controller with return values
    save_ctrl = SimpleNamespace()
    calls = []
    def save_default():
        calls.append('default')
        return 'default'
    def save_active():
        calls.append('active')
        return 'active'
    save_ctrl.save_default = save_default
    save_ctrl.save_active = save_active
    parent = SimpleNamespace(save_controller=save_ctrl)
    editor_controller = SimpleNamespace(model=None)
    ctrl = GridController(editor_controller, parent)
    return ctrl, calls


def test_save_default_calls_and_returns(setup_controller):
    ctrl, calls = setup_controller
    result = ctrl._save_default()
    assert result == 'default'
    assert calls == ['default']


def test_save_active_calls_and_returns(setup_controller):
    ctrl, calls = setup_controller
    result = ctrl._save_active()
    assert result == 'active'
    assert calls == ['active']
