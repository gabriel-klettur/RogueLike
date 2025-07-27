import pytest
from roguelike_editors.inventory.left_panel.list.list_model import ListModel


def test_default_selected_eid():
    m = ListModel()
    assert m.selected_eid is None


def test_assign_selected_eid():
    m = ListModel()
    m.selected_eid = 'entity1'
    assert m.selected_eid == 'entity1'


def test_independent_instances():
    m1 = ListModel()
    m2 = ListModel()
    m1.selected_eid = 'e1'
    assert m2.selected_eid is None
