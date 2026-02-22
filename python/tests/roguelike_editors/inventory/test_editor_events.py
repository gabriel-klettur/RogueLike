import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.editor_events import InventoryEditorEventHandler


def test_handle_event_first_handler_true():
    ctrl = SimpleNamespace(
        inventory_panel_event_handler=SimpleNamespace(handle=lambda e: True),
        item_selection_event_handler=SimpleNamespace(handle=lambda e: (_ for _ in ()).throw(Exception("Should not be called"))),
        grid_event_handler=SimpleNamespace(handle=lambda e: (_ for _ in ()).throw(Exception("Should not be called"))),
        model=None,
        world=None,
        view=None
    )
    handler = InventoryEditorEventHandler(ctrl)
    assert handler.handle('evt') is True


def test_handle_event_second_handler_true():
    ctrl = SimpleNamespace(
        inventory_panel_event_handler=SimpleNamespace(handle=lambda e: False),
        item_selection_event_handler=SimpleNamespace(handle=lambda e: True),
        grid_event_handler=SimpleNamespace(handle=lambda e: (_ for _ in ()).throw(Exception("Should not be called"))),
        model=None,
        world=None,
        view=None
    )
    handler = InventoryEditorEventHandler(ctrl)
    assert handler.handle('evt') is True


def test_handle_event_third_handler_true():
    ctrl = SimpleNamespace(
        inventory_panel_event_handler=SimpleNamespace(handle=lambda e: False),
        item_selection_event_handler=SimpleNamespace(handle=lambda e: False),
        grid_event_handler=SimpleNamespace(handle=lambda e: True),
        model=None,
        world=None,
        view=None
    )
    handler = InventoryEditorEventHandler(ctrl)
    assert handler.handle(123) is True


def test_handle_event_all_false():
    ctrl = SimpleNamespace(
        inventory_panel_event_handler=SimpleNamespace(handle=lambda e: False),
        item_selection_event_handler=SimpleNamespace(handle=lambda e: False),
        grid_event_handler=SimpleNamespace(handle=lambda e: False),
        model=None,
        world=None,
        view=None
    )
    handler = InventoryEditorEventHandler(ctrl)
    assert handler.handle(None) is False
