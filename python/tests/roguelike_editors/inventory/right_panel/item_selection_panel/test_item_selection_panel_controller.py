import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

@pytest.fixture
def setup_ctrl():
    model = ItemSelectionPanelModel(['init'], visible_count=3)
    ctrl = ItemSelectionPanelController(model)
    return ctrl, model


def test_open_delegates_to_title_controller(setup_ctrl):
    ctrl, model = setup_ctrl
    stub = SimpleNamespace()
    called = {}
    def open_stub(default_items, ground_items):
        called['args'] = (default_items, ground_items)
    stub.open = open_stub
    ctrl.title_controller = stub
    default = ['a', 'b']
    ground = ['g']
    ctrl.open(default, ground)
    assert called['args'] == (default, ground)


def test_close_delegates_to_title_controller(setup_ctrl):
    ctrl, model = setup_ctrl
    stub = SimpleNamespace(called=False)
    stub.close = lambda: setattr(stub, 'called', True)
    ctrl.title_controller = stub
    ctrl.close()
    assert stub.called is True


def test_select_item_delegates_to_list_controller(setup_ctrl):
    ctrl, model = setup_ctrl
    stub = SimpleNamespace(called=None)
    stub.select_item = lambda item: setattr(stub, 'called', item)
    ctrl.list_controller = stub
    ctrl.select_item('item1')
    assert stub.called == 'item1'


def test_change_tab_delegates_to_tabs_controller(setup_ctrl):
    ctrl, model = setup_ctrl
    stub = SimpleNamespace(called=None)
    stub.change_tab = lambda tab: setattr(stub, 'called', tab)
    ctrl.tabs_controller = stub
    ctrl.change_tab('ground')
    assert stub.called == 'ground'


def test_set_quantity_delegates_to_input_controller(setup_ctrl):
    ctrl, model = setup_ctrl
    stub = SimpleNamespace(called=None)
    stub.set_quantity = lambda val: setattr(stub, 'called', val)
    ctrl.input_controller = stub
    ctrl.set_quantity('5')
    assert stub.called == '5'


def test_confirm_delegates_to_button_controller_and_returns_value(setup_ctrl):
    ctrl, model = setup_ctrl
    stub = SimpleNamespace(confirm=lambda: ('itm', 7))
    ctrl.button_controller = stub
    result = ctrl.confirm()
    assert result == ('itm', 7)
