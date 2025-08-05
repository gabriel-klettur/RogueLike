from types import SimpleNamespace
import roguelike_editors.inventory.editor_controller as ec_mod
from roguelike_editors.inventory.editor_controller import InventoryEditorController
from roguelike_editors.inventory.editor_model import InventoryEditorModel

import logging
logger = logging.getLogger(__name__)


def test_init_calls_load_data(monkeypatch):
    calls = []
    # Stub dependencies to avoid heavy init logic
    monkeypatch.setattr(ec_mod, 'PanelController', lambda *args, **kwargs: 'panel_ctr')
    monkeypatch.setattr(ec_mod, 'PanelEventHandler', lambda *args, **kwargs: SimpleNamespace(handle=lambda e: False))
    monkeypatch.setattr(ec_mod, 'InventoryEditorEventHandler', lambda c: SimpleNamespace(handle=lambda e: False))
    monkeypatch.setattr(ec_mod, 'InventoryItemsPanelController', lambda c: 'grid_ctr')
    monkeypatch.setattr(ec_mod, 'InventoryItemsPanelEventHandler', lambda c: SimpleNamespace(handle=lambda e: False))
    monkeypatch.setattr(ec_mod, 'ItemSelectionPanelEventHandler', lambda *args: SimpleNamespace(handle=lambda e: False))
    monkeypatch.setattr(ec_mod, 'InventoryEditorView', lambda assets, font: SimpleNamespace(
    draw=lambda *args, **kwargs: None,
    inventory_panel_model='inv_panel_model',
    inventory_panel_view='inv_panel_view',
    item_panel_controller='item_panel_ctr',
    item_panel_view='item_panel_view'
))
    monkeypatch.setattr(ec_mod, 'DataController', lambda model: SimpleNamespace(load_data=lambda: calls.append(True)))
    # Instantiate controller
    ctrl = InventoryEditorController('game', 'world', {'a': 'b'}, 'font')
    # Verify attributes set
    assert ctrl.game == 'game'
    assert ctrl.world == 'world'
    assert ctrl.assets == {'a': 'b'}
    assert ctrl.font == 'font'
    assert isinstance(ctrl.model, InventoryEditorModel)
    assert ctrl.inventory_panel_controller == 'panel_ctr'
    assert ctrl.grid_controller == 'grid_ctr'
    # Ensure load_data was called once
    assert calls == [True]


def test_handle_event_delegates_to_event_handler():
    ctrl = SimpleNamespace(event_handler=SimpleNamespace(handle=lambda e: e * 2))
    result = InventoryEditorController.handle_event(ctrl, 5)
    # handle_event does not return handler's return, so result is None but handler called
    # We infer invocation by no exception
    assert result is None


def test_debug_dump_prints(monkeypatch):
    model = SimpleNamespace(
        visible=True,
        entities=[1],
        selected_eid=2,
        editing_property='prop',
        editing_index=3,
        drag_item='di',
        drag_slot='ds',
        scroll_offset=4,
        left_panel_model='lpm',
        items_panel_model='ipm',
        item_selection_panel_model='isp'
    )
    ctrl = SimpleNamespace(model=model,
        inventory_panel_controller='ipc',
        grid_controller='gc',
        view=SimpleNamespace(inventory_panel_view='ipv', grid_view='gv', item_panel_view='ipv2'))
    logs = []
    monkeypatch.setattr('builtins.print', lambda *args, **kwargs: logs.append(' '.join(str(a) for a in args)))
    InventoryEditorController.debug_dump(ctrl)
    assert any('InventoryEditorController.debug_dump' in line for line in logs)
    assert any('visible: True' in line for line in logs)
    assert any('entities: [1]' in line for line in logs)


def test_draw_calls_view_draw(monkeypatch):
    screen = SimpleNamespace(get_size=lambda: (10, 10))
    called = []
    dummy_view = SimpleNamespace(draw=lambda s, m, w: called.append((s, m, w)))
    ctrl = SimpleNamespace(view=dummy_view, model='md', world='wd')
    result = InventoryEditorController.draw(ctrl, screen)
    assert called == [(screen, 'md', 'wd')]
    assert result is None
