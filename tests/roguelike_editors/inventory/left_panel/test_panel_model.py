import pytest
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel


def test_default_models_and_focus():
    m = InventoryPanelModel()
    # Default tabs_model and list_model
    assert m.tabs_model is not None
    assert m.list_model is not None
    # Default categories and selected_eid
    assert m.categories == ['player', 'hostile', 'map']
    assert m.current_category == 'player'
    assert m.selected_eid is None
    # Default camera focus
    assert m.camera_focus_target is None


def test_properties_delegate_to_models():
    m = InventoryPanelModel()
    # categories property
    m.categories = ['a', 'b']
    assert m.tabs_model.categories == ['a', 'b']
    assert m.categories == ['a', 'b']
    # current_category property
    m.current_category = 'b'
    assert m.tabs_model.current_category == 'b'
    assert m.current_category == 'b'
    # selected_eid property
    m.selected_eid = 'ent2'
    assert m.list_model.selected_eid == 'ent2'
    assert m.selected_eid == 'ent2'
    # camera_focus_target property
    m.camera_focus_target = {'x':1}
    assert m.camera_focus_target == {'x':1}
