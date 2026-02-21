import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.editor_model import InventoryEditorModel
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_model import InventoryitemsPanelModel
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel
import roguelike_editors.inventory.editor_model as model_mod


def test_default_values():
    model = InventoryEditorModel()
    assert model.visible is False
    assert model.default_data == {}
    assert model.active_data == {}
    assert model.editing_property is None
    assert model.editing_index is None
    assert model.entities is None
    assert model.drag_item is None
    assert model.drag_slot is None
    assert model.prev_left is False
    assert model.prev_right is False
    assert model.scroll_offset == 0
    assert isinstance(model.left_panel_model, InventoryPanelModel)
    assert isinstance(model.items_panel_model, InventoryitemsPanelModel)
    assert isinstance(model.item_selection_panel_model, ItemSelectionPanelModel)


def test_editing_side_property():
    model = InventoryEditorModel()
    default_tab = model.items_panel_model.tabs.active_tab
    assert model.editing_side == default_tab
    model.editing_side = 'ground'
    assert model.items_panel_model.tabs.active_tab == 'ground'
    assert model.editing_side == 'ground'


def test_grid_model_property():
    model = InventoryEditorModel()
    assert model.grid_model is model.items_panel_model


def test_categories_property():
    model = InventoryEditorModel()
    model.categories = ['a', 'b']
    assert model.left_panel_model.categories == ['a', 'b']
    assert model.categories == ['a', 'b']


def test_current_category_property_prints(monkeypatch):
    model = InventoryEditorModel()
    calls = []
    monkeypatch.setattr(model_mod.logger, 'debug', lambda msg: calls.append(msg))
    model.current_category = 'x'
    assert model.left_panel_model.current_category == 'x'
    assert any('InventoryEditorModel.current_category set to x' in msg for msg in calls)


def test_selected_eid_property_prints(monkeypatch):
    model = InventoryEditorModel()
    calls = []
    monkeypatch.setattr(model_mod.logger, 'debug', lambda msg: calls.append(msg))
    model.selected_eid = 5
    assert model.left_panel_model.selected_eid == 5
    assert any('InventoryEditorModel.selected_eid set to 5' in msg for msg in calls)


def test_camera_focus_target_property():
    model = InventoryEditorModel()
    model.camera_focus_target = 'target'
    assert model.left_panel_model.camera_focus_target == 'target'
    assert model.camera_focus_target == 'target'
