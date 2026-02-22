import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_view import TilePickerView
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, COLS, BTN_H

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.display.set_mode((1, 1))
    yield
    pygame.quit()

@ pytest.fixture
def setup_view(monkeypatch):
    # Dummy controller and state
    controller = SimpleNamespace()
    picker_state = SimpleNamespace(scroll_offset=3, pos=(5, 5), open=False, config_mode=False, config_src_idx=None, current_choice=None)
    assets = []
    # Monkeypatch ScrollableGrid
    class DummyGrid:
        def __init__(self, thumb, pad, count, scroll, cols):
            pass
        def compute(self):
            return (COLS*3, 1, 80, 20)
        def draw_items(self, surface, assets, panel_pos, draw_fn):
            return None
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.ScrollableGrid',
        DummyGrid
    )
    view = TilePickerView(controller, picker_state, assets)
    # Provide dummy panel for local coords
    view.panel = SimpleNamespace(surface=pygame.Surface((100, 100)), pos=(10, 10))
    return view


def test_init(setup_view):
    view = setup_view
    assert hasattr(view, 'controller')
    assert hasattr(view, 'picker_state')
    assert hasattr(view, 'assets')
    assert view.panel.pos == (10, 10)
    assert hasattr(view, 'tileset_text_input')
    assert hasattr(view, 'selection_overlay')


def test_ellipsize(setup_view):
    view = setup_view
    class Font:
        def size(self, text):
            return (len(text) * 5, 0)
    # No truncation
    assert view._ellipsize('ab', Font(), 15) == 'ab'
    # Truncation
    result = view._ellipsize('abc', Font(), 10)
    assert result.endswith('...')
    assert len(result) < len('abc') + 3


def test_compute_layout(setup_view):
    view = setup_view
    grid, cols, rows, w, h_grid, h = view._compute_layout()
    expected_h = h_grid + PAD + BTN_H + PAD
    assert cols == COLS*3
    assert rows == 1
    assert w == 80
    assert h_grid == 20
    assert h == expected_h


def test_get_local_coords(monkeypatch, setup_view):
    view = setup_view
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.pygame.mouse.get_pos',
        lambda: (50, 60)
    )
    view.picker_state.scroll_offset = 7
    view.panel.pos = (15, 20)
    lx, ly, y0 = view._get_local_coords()
    assert lx == 50 - 15
    assert ly == 60 - 20
    assert y0 == PAD - 7


def test_render_not_open(setup_view):
    view = setup_view
    view.picker_state.open = False
    screen = pygame.Surface((100, 100))
    # Should not error or draw
    assert view.render(screen) is None


def test_draw_assets_grid_config_mode_no_hover(setup_view, monkeypatch):
    view = setup_view
    view.picker_state.config_mode = True
    view.picker_state.scroll_offset = 0
    surf = pygame.Surface((THUMB, THUMB))
    view.assets = [("val", surf, False, (3, 4))]
    # Position mouse outside cell
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.pygame.mouse.get_pos',
        lambda: (0, 0)
    )
    class DummyGrid:
        def __init__(self, *args, **kwargs): pass
        def compute(self): return (1, 1, THUMB, THUMB)
        def draw_items(self, *args, **kwargs):
            raise AssertionError("Should not call fallback in config_mode")
    result = view._draw_assets_grid(DummyGrid())
    assert result == (None, None)


def test_draw_assets_grid_config_mode_hover_returns_asset(setup_view, monkeypatch):
    view = setup_view
    view.picker_state.config_mode = True
    view.picker_state.scroll_offset = 0
    surf = pygame.Surface((THUMB, THUMB))
    view.assets = [("val", surf, False, (3, 4))]
    # Position mouse inside first cell (panel.pos is (10,10))
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.pygame.mouse.get_pos',
        lambda: (view.panel.pos[0] + 1, view.panel.pos[1] + 1)
    )
    class DummyGrid2:
        def __init__(self, *args, **kwargs): pass
        def compute(self): return (1, 1, THUMB, THUMB)
        def draw_items(self, *args, **kwargs):
            raise AssertionError("Should not call fallback in config_mode")
    result = view._draw_assets_grid(DummyGrid2())
    assert result == ("val", (3, 4))


def test_draw_assets_grid_default_mode_hover(setup_view, monkeypatch):
    view = setup_view
    view.picker_state.config_mode = False
    view.picker_state.scroll_offset = 0
    surf = pygame.Surface((THUMB, THUMB))
    view.assets = [("val", surf, False, (3, 4))]
    # Position mouse inside first cell
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.pygame.mouse.get_pos',
        lambda: (view.panel.pos[0] + 1, view.panel.pos[1] + 1)
    )
    class DummyGrid3:
        def __init__(self, *args, **kwargs): pass
        def compute(self): return (1, 1, THUMB, THUMB)
        def draw_items(self, *args, **kwargs):
            raise AssertionError("Fallback should not be called when hover_idx present")
    result = view._draw_assets_grid(DummyGrid3())
    assert result == ("val", (3, 4))


def test_draw_assets_grid_default_mode_no_hover(setup_view, monkeypatch):
    view = setup_view
    view.picker_state.config_mode = False
    view.picker_state.scroll_offset = 0
    surf = pygame.Surface((THUMB, THUMB))
    view.assets = [("val", surf, False, (3, 4))]
    # Position mouse outside cell
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.pygame.mouse.get_pos',
        lambda: (0, 0)
    )
    class DummyGrid4:
        def __init__(self, *args, **kwargs): pass
        def compute(self): return (1, 1, THUMB, THUMB)
        def draw_items(self, *args, **kwargs):
            raise AssertionError("Fallback should not be called; returns early")
    result = view._draw_assets_grid(DummyGrid4())
    assert result == (None, None)
