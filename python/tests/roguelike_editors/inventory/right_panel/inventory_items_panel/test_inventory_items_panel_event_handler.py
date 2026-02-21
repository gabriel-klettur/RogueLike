import pytest
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_events import InventoryItemsPanelEventHandler

class DummyHandler:
    def __init__(self, should_handle=False, to_raise=False):
        self.should_handle = should_handle
        self.to_raise = to_raise
        self.calls = 0
    def handle(self, event):
        self.calls += 1
        if self.to_raise:
            raise Exception("error")
        return self.should_handle

@pytest.fixture
def setup_event_handler(monkeypatch):
     # Stub __init__ to avoid sub-handlers requiring full controller
     def fake_init(self, grid_controller):
         self.grid_controller = grid_controller
         self.handlers = []
     monkeypatch.setattr(InventoryItemsPanelEventHandler, "__init__", fake_init)
     eph = InventoryItemsPanelEventHandler(grid_controller=None)
     return eph


def test_handle_returns_false_when_no_handlers(setup_event_handler):
    eph = setup_event_handler
    # Empty handlers
    eph.handlers = []
    assert eph.handle('event') is False


def test_handle_true_when_first_handler_returns_true(setup_event_handler):
    eph = setup_event_handler
    h1 = DummyHandler(should_handle=True)
    h2 = DummyHandler(should_handle=True)
    eph.handlers = [h1, h2]
    result = eph.handle('event')
    assert result is True
    assert h1.calls == 1
    assert h2.calls == 0


def test_handle_true_when_later_handler_returns_true(setup_event_handler):
    eph = setup_event_handler
    h1 = DummyHandler(should_handle=False)
    h2 = DummyHandler(should_handle=True)
    eph.handlers = [h1, h2]
    result = eph.handle('event')
    assert result is True
    assert h1.calls == 1
    assert h2.calls == 1


def test_exceptions_are_caught_and_continue(setup_event_handler):
    eph = setup_event_handler
    h1 = DummyHandler(to_raise=True)
    h2 = DummyHandler(should_handle=True)
    eph.handlers = [h1, h2]
    result = eph.handle('event')
    assert result is True
    assert h1.calls == 1
    assert h2.calls == 1
