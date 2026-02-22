import pytest
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_view_panel.tiles_view_controller import TilesViewPanelController

@pytest.fixture
def controller(monkeypatch):
    # Stub view to avoid UI dependencies
    called = {}
    class DummyView:
        def __init__(self, ctrl, state):
            self.ctrl_arg = ctrl
            self.state_arg = state
        def render(self, screen, camera, game_map):
            called['args'] = (screen, camera, game_map)

    monkeypatch.setattr(
         'roguelike_editors.tiles.tiles_view_panel.tiles_view_controller.TilesViewPanelView',
         DummyView
     )

    # Dummy editor state and controller
    editor_state = SimpleNamespace(selected_tile=None, current_choice=None, current_layer=None)
    editor_controller = SimpleNamespace(editor=editor_state, _tile_under_mouse=lambda mp, cam, gm: 'tile')
    state = SimpleNamespace(dragging=False, drag_offset=(0, 0), pos=None, size=(0, 0))

    ctrl = TilesViewPanelController(editor_controller, state)
    return ctrl, editor_controller, state, called


def test_init_properties(controller):
    ctrl, editor_controller, state, called = controller
    assert ctrl.editor_controller is editor_controller
    assert ctrl.editor_state is editor_controller.editor
    assert ctrl.state is state
    # View should be DummyView and receive ctrl and state
    assert hasattr(ctrl, 'view')
    assert isinstance(ctrl.view, object)
    assert ctrl.view.ctrl_arg is ctrl
    assert ctrl.view.state_arg is state


def test_render_delegates_to_view(controller):
    ctrl, editor_controller, state, called = controller
    screen = object()
    camera = object()
    game_map = object()
    ctrl.render(screen, camera, game_map)
    assert 'args' in called
    assert called['args'] == (screen, camera, game_map)


def test_drag_and_stop_drag(controller):
    ctrl, editor_controller, state, called = controller
    # Test drag update
    state.dragging = True
    state.drag_offset = (3, 4)
    ctrl.drag((10, 12))
    assert state.pos == (7, 8)
    # Test stop_drag resets dragging
    state.dragging = True
    ctrl.stop_drag()
    assert state.dragging is False


def test_tile_under_mouse(controller):
    ctrl, editor_controller, state, called = controller
    res = ctrl._tile_under_mouse((1, 2), 'cam', 'map')
    assert res == 'tile'
