import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_events import ItemSelectionPanelEventHandler

@pytest.fixture
def setup_handler():
    model = SimpleNamespace(show_panel=False)
    controller = SimpleNamespace(model=model)
    view = SimpleNamespace()
    grid_controller = object()
    handler = ItemSelectionPanelEventHandler(grid_controller, controller, view)
    return handler, model


def test_handle_returns_false_when_panel_hidden(setup_handler):
    handler, model = setup_handler
    event = SimpleNamespace()
    # Panel hidden should not delegate
    model.show_panel = False
    # Override handlers to failure if called
    handler.handlers = [SimpleNamespace(handle=lambda e: (_ for _ in ()).throw(Exception("Should not be called")))]
    assert handler.handle(event) is False


def test_handle_returns_true_on_first_handler_that_handles(setup_handler):
    handler, model = setup_handler
    event = SimpleNamespace()
    model.show_panel = True
    called = []
    stub1 = SimpleNamespace(handle=lambda e: False)
    def h2(e): called.append('h2'); return True
    stub2 = SimpleNamespace(handle=h2)
    stub3 = SimpleNamespace(handle=lambda e: (_ for _ in ()).throw(Exception("Should not reach stub3")))
    handler.handlers = [stub1, stub2, stub3]
    result = handler.handle(event)
    assert result is True
    assert called == ['h2']


def test_handle_returns_false_when_no_handler_handles(setup_handler):
    handler, model = setup_handler
    event = SimpleNamespace()
    model.show_panel = True
    handler.handlers = [SimpleNamespace(handle=lambda e: False), SimpleNamespace(handle=lambda e: False)]
    assert handler.handle(event) is False
