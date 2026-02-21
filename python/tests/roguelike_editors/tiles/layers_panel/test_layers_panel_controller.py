import pytest
from roguelike_editors.tiles.layers_panel.layers_panel_controller import LayersPanelController
from roguelike_editors.tiles.layers_panel.layers_panel_states import LayersPanelState


class DummyToolbarState:
    def __init__(self, visible_layers):
        self.visible_layers = visible_layers


class DummyEditorState:
    def __init__(self, visible_layers):
        self.toolbar_state = DummyToolbarState(visible_layers)


class DummyEditorController:
    def __init__(self, visible_layers):
        self.editor = DummyEditorState(visible_layers)


def test_init_copies_visible_layers():
    original = {'a': True, 'b': False}
    state = LayersPanelState()
    controller = LayersPanelController(DummyEditorController(original), state)
    # state.visible_layers matches and is a copy
    assert state.visible_layers == original
    original['a'] = False
    assert state.visible_layers['a'] == True


def test_drag_updates_position_when_dragging_true():
    state = LayersPanelState()
    state.dragging = True
    state.drag_offset = (5, 5)
    controller = LayersPanelController(DummyEditorController({}), state)
    controller.drag((10, 15))
    assert state.pos == (5, 10)


def test_drag_does_nothing_when_not_dragging():
    state = LayersPanelState()
    state.dragging = False
    state.pos = None
    controller = LayersPanelController(DummyEditorController({}), state)
    controller.drag((10, 15))
    assert state.pos is None


def test_stop_drag_sets_dragging_false():
    state = LayersPanelState()
    state.dragging = True
    controller = LayersPanelController(DummyEditorController({}), state)
    controller.stop_drag()
    assert state.dragging is False
