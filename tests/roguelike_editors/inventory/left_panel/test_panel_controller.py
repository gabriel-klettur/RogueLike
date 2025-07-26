import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.left_panel.panel_controller import PanelController


def test_change_category_delegation():
    ec = SimpleNamespace()
    model = SimpleNamespace()
    pc = PanelController(ec, model)
    # stub tabs_controller
    called = {}
    pc.tabs_controller.change_category = lambda cat: called.setdefault('cat', cat)
    pc.change_category('my_cat')
    assert called.get('cat') == 'my_cat'


def test_select_entity_delegation():
    ec = SimpleNamespace()
    model = SimpleNamespace()
    pc = PanelController(ec, model)
    called = {}
    pc.list_controller.select_entity = lambda eid: called.setdefault('eid', eid)
    pc.select_entity('EID1')
    assert called.get('eid') == 'EID1'


def test_get_items_list_delegation():
    ec = SimpleNamespace()
    model = SimpleNamespace()
    pc = PanelController(ec, model)
    pc.list_controller.get_items_list = lambda: [1, 2, 3]
    assert pc.get_items_list() == [1, 2, 3]
