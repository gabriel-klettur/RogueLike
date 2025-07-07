import pytest
from roguelike_editors.inventory.model.editor_model import InventoryEditorModel


def test_default_model():
    model = InventoryEditorModel()
    assert not model.visible
    assert model.entities is None
    assert model.selected_eid is None
    assert model.drag_item is None
    assert model.drag_slot is None
    assert model.prev_left is False
    assert model.prev_right is False
