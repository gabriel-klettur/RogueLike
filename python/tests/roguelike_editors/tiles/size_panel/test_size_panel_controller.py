import pytest
from roguelike_editors.tiles.size_panel.size_panel_controller import SizePanelController
from roguelike_editors.tiles.size_panel.size_panel_state import SizePanelState

class DummyControllerForDrag:
    def __init__(self):
        self.drag_called = False
        self.drag_pos = None
        self.stop_called = False

    def drag(self, pos):
        self.drag_called = True
        self.drag_pos = pos

    def stop_drag(self):
        self.stop_called = True


def test_toggle_show_hide():
    state = SizePanelState()
    controller = SizePanelController(None, state)
    assert state.visible is False
    controller.toggle()
    assert state.visible is True
    controller.toggle()
    assert state.visible is False
    controller.show()
    assert state.visible is True
    controller.hide()
    assert state.visible is False


def test_drag_updates_pos_when_dragging():
    state = SizePanelState()
    state.dragging = True
    state.drag_offset = (2, 3)
    controller = SizePanelController(None, state)
    controller.drag((10, 15))
    assert state.pos == (8, 12)


def test_drag_does_nothing_when_not_dragging():
    state = SizePanelState()
    state.dragging = False
    controller = SizePanelController(None, state)
    controller.drag((10, 15))
    assert state.pos is None


def test_stop_drag():
    state = SizePanelState()
    state.dragging = True
    controller = SizePanelController(None, state)
    controller.stop_drag()
    assert state.dragging is False


def test_on_size_selected_valid_and_invalid():
    state = SizePanelState()
    controller = SizePanelController(None, state)
    controller.on_size_selected(3)
    assert state.selected_index == 3
    old = state.selected_index
    controller.on_size_selected(len(state.sizes))
    assert state.selected_index == old
