import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_view_panel.tiles_view_view import TilesViewPanelView
from roguelike_engine.config.config_tiles import TILE_SIZE

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_view(monkeypatch):
    # Dummy DraggablePanel
    class DummyPanel:
        def __init__(self, w, h):
            self.surface = pygame.Surface((w, h))
            self.pos = None
        def resize(self, w, h):
            self.surface = pygame.Surface((w, h))
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_view_panel.tiles_view_view.DraggablePanel',
        DummyPanel
    )
    # Dummy ScrollableGrid
    class DummyGrid:
        def __init__(self, *args, **kwargs): pass
        def draw_items(self, surf, items, pos, func): pass
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_view_panel.tiles_view_view.ScrollableGrid',
        DummyGrid
    )
    # Dummy ListPanelUI
    class DummyList:
        def __init__(self, *args, **kwargs): pass
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_view_panel.tiles_view_view.ListPanelUI',
        DummyList
    )
    # Stub load_image
    monkeypatch.setattr(
        'roguelike_engine.utils.loader.load_image',
        lambda path, size: pygame.Surface(size)
    )
    # Stub font
    monkeypatch.setattr(
        'pygame.font.SysFont',
        lambda name, size, bold=False: pygame.font.Font(None, size)
    )
    # Dummy controller and state
    toolbar_info = SimpleNamespace(x=1, y=2, size=20, padding=5)
    editor_state = SimpleNamespace(
        selected_tile=SimpleNamespace(sprite=pygame.Surface((TILE_SIZE, TILE_SIZE))),
        current_choice=None,
        current_layer=SimpleNamespace(value=2, name='TestLayer')
    )
    editor_controller = SimpleNamespace(
        editor_state=editor_state,
        toolbar=toolbar_info
    )
    state = SimpleNamespace(pos=None)
    view = TilesViewPanelView(editor_controller, state)
    return view, editor_controller, state


def test_init_sets_panel_and_state(setup_view):
    view, ctrl, state = setup_view
    assert hasattr(view, 'panel')
    assert isinstance(view.panel, object)
    # Test override pos
    state.pos = (10, 15)
    view2 = TilesViewPanelView(ctrl, state)
    assert view2.panel.pos == (10, 15)


def test_screen_to_world_and_compute_panel_position(setup_view):
    view, ctrl, state = setup_view
    # Test screen_to_world
    camera = SimpleNamespace(zoom=2, offset_x=3, offset_y=5)
    # Use mouse_pos such that world coords are known
    mouse_pos = (10, 14)
    col, row = view._screen_to_world(mouse_pos, camera)
    wx = mouse_pos[0] / camera.zoom + camera.offset_x
    wy = mouse_pos[1] / camera.zoom + camera.offset_y
    assert (col, row) == (int(wx) // TILE_SIZE, int(wy) // TILE_SIZE)
    # Test compute_panel_position override
    state.pos = (7, 8)
    screen = SimpleNamespace(get_size=lambda: (200, 100))
    assert view._compute_panel_position(screen, 50, 60) == (7, 8)
    # Test top-right modes
    state.pos = None
    ctrl.editor_state.current_tool = 'brush'
    assert view._compute_panel_position(screen, 50, 60) == (200 - 50 - 12, 12)
    # Test side-of-toolbar
    ctrl.editor_state.current_tool = 'view'
    toolbar = ctrl.toolbar
    assert view._compute_panel_position(screen, 50, 60) == (toolbar.x + toolbar.size + toolbar.padding, toolbar.y)
