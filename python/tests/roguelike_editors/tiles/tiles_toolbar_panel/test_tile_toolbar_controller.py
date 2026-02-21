import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_controller import TileToolbarController

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def controller(monkeypatch):
    # Stub icon loading to avoid file I/O
    monkeypatch.setattr(TileToolbarController, '_load_icons', lambda self: {})
    # Dummy size_panel_controller
    size_ctrl = SimpleNamespace()
    size_ctrl.state = SimpleNamespace(visible=False)
    size_ctrl.show = lambda: setattr(size_ctrl.state, 'visible', True)
    size_ctrl.toggle = lambda: setattr(size_ctrl.state, 'visible', not size_ctrl.state.visible)
    # Editor state and controller
    editor_state = SimpleNamespace(
        toolbar_state=SimpleNamespace(),
        current_tool=None,
        picker_state=SimpleNamespace(open=False),
        selected_tile=None
    )
    editor_controller = SimpleNamespace(editor=editor_state, size_panel_controller=size_ctrl)
    ctrl = TileToolbarController(editor_controller)
    return ctrl, editor_state


def test_init_properties(controller):
    ctrl, editor_state = controller
    assert ctrl.editor_state is editor_state
    assert isinstance(ctrl.icons, dict)
    assert hasattr(ctrl, 'view')
    assert isinstance(ctrl.icon_rects, dict)


def test_select_tile_sets_choice_and_tool(controller):
    ctrl, editor_state = controller
    ctrl.select_tile('grass')
    assert editor_state.current_choice.endswith('grass.png')
    assert editor_state.current_tool == 'brush'


def test_drag_and_stop_drag(controller):
    ctrl, editor_state = controller
    ts = SimpleNamespace(dragging=True, drag_offset=(2, 3), pos=(0, 0))
    editor_state.toolbar_state = ts
    ctrl.drag((5, 7))
    assert ts.pos == (3, 4)  # 5-2, 7-3
    ctrl.stop_drag()
    assert editor_state.toolbar_state.dragging is False
