import pytest
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.tabs.tabs_controller import TabsController

@pytest.fixture
def setup_controller():
    model = SimpleNamespace(editing_side=None)
    editor_controller = SimpleNamespace(model=model)
    parent = SimpleNamespace()
    controller = TabsController(editor_controller, parent)
    return controller, model


def test_show_default_sets_editing_side_default(setup_controller):
    controller, model = setup_controller
    controller.show_default()
    assert model.editing_side == 'default'


def test_show_active_sets_editing_side_active(setup_controller):
    controller, model = setup_controller
    controller.show_active()
    assert model.editing_side == 'active'
