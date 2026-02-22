import pytest
import pygame
from pathlib import Path
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_controller import TilePickerController

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@ pytest.fixture
def setup_controller(monkeypatch):
    # Stub out asset loading and positions
    monkeypatch.setattr(TilePickerController, '_load_assets', lambda self: setattr(self, 'assets', []))
    monkeypatch.setattr(TilePickerController, '_load_positions', lambda self: None)
    editor_ctrl = SimpleNamespace()
    editor_state = SimpleNamespace(scroll_offset=5)
    picker_state = SimpleNamespace(open=False)
    controller = TilePickerController(editor_ctrl, editor_state, picker_state)
    return controller, editor_state, picker_state


def test_init_properties(setup_controller):
    controller, editor_state, picker_state = setup_controller
    assert controller.editor_controller is controller.editor_controller
    assert controller.editor_state is editor_state
    assert hasattr(controller, 'view')
    # base_dir and current_dir are Paths and equal on init
    assert isinstance(controller.base_dir, Path)
    assert controller.current_dir == controller.base_dir


def test_swap_positions_updates_assets_and_view(setup_controller):
    controller, *_ = setup_controller
    # Prepare assets
    controller.assets = [('a', None, False, None), ('b', None, False, None)]
    controller.view.assets = controller.assets
    controller.swap_positions(0, 1)
    assert controller.assets == [('b', None, False, None), ('a', None, False, None)]
    assert controller.view.assets == controller.assets


def test_swap_positions_invalidates_cache(setup_controller, monkeypatch):
    controller, *_ = setup_controller
    # Prepare assets and fake cache attributes
    controller.assets = [('a', None, False, None), ('b', None, False, None)]
    controller.view.assets = controller.assets
    controller.view.assets_cache_surf = object()
    controller.view.assets_cache_size = (2, 1)
    # Prevent actual file I/O
    import builtins, io, json
    monkeypatch.setattr(builtins, 'open', lambda *args, **kwargs: io.StringIO())
    monkeypatch.setattr(json, 'dump', lambda *args, **kwargs: None)
    # Perform swap
    controller.swap_positions(0, 1)
    # Cache attributes should be removed
    assert not hasattr(controller.view, 'assets_cache_surf')
    assert not hasattr(controller.view, 'assets_cache_size')


def test_is_over_true_and_false(setup_controller):
    controller, *_ = setup_controller
    # Setup picker_state surface and pos
    controller.picker_state.surface = pygame.Surface((10, 10))
    controller.picker_state.pos = (5, 5)
    assert controller.is_over((6, 6))
    assert not controller.is_over((0, 0))


def test_drag_and_stop_drag(setup_controller):
    controller, *_ = setup_controller
    # Setup dragging state
    controller.picker_state.dragging = True
    controller.picker_state.drag_offset = (2, 3)
    controller.drag((10, 13))
    assert controller.picker_state.pos == (8, 10)
    controller.stop_drag()
    assert controller.picker_state.dragging is False


def test_scroll_adjusts_scroll_offset(setup_controller):
    controller, editor_state, _ = setup_controller
    controller.scroll(1)
    assert editor_state.scroll_offset == max(0, 5 - 30)


def test_open_and_close_calls_load_and_resets_state(setup_controller):
    controller, editor_state, picker_state = setup_controller
    calls = []
    # Override load methods to track calls
    def load_assets(self): calls.append('assets')
    def load_positions(self): calls.append('positions')
    setattr(controller, '_load_assets', load_assets.__get__(controller, type(controller)))
    setattr(controller, '_load_positions', load_positions.__get__(controller, type(controller)))
    # Modify initial state
    picker_state.current_choice = 'x'
    picker_state.dragging = True
    editor_state.scroll_offset = 7
    # Open picker
    controller.open()
    assert picker_state.open is True
    assert picker_state.current_choice is None
    assert picker_state.dragging is False
    assert editor_state.scroll_offset == 0
    assert calls == ['assets', 'positions']
    # Close picker
    controller._close()
    assert picker_state.open is False
    assert picker_state.current_choice is None
    assert picker_state.dragging is False
