import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_view import TilesCollisionPanelView
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@ pytest.fixture
def setup_view(monkeypatch):
    # Prepare dummy controller and state
    controller = SimpleNamespace()
    state = SimpleNamespace()
    state.option_rects = {}
    # Toolbar state required by view
    controller.editor_state = SimpleNamespace(toolbar_state=SimpleNamespace(collision_picker_open=False, collision_choice=None, collision_picker_pos=None, collision_picker_panel_size=None))
    # Editor controller required for compute_position
    controller.editor_controller = SimpleNamespace(
        toolbar=SimpleNamespace(icon_rects={}, padding=0),
        view_panel_controller=SimpleNamespace(state=None)
    )
    return TilesCollisionPanelView(controller, state)


def test_init(setup_view):
    view = setup_view
    assert hasattr(view, 'options')
    assert view.options == [('#', 'Collision'), ('.', 'Walk')]
    assert hasattr(view, 'panel')


def test_compute_dimensions(monkeypatch, setup_view):
    view = setup_view
    # Monkeypatch font to return known height
    class DummyFont:
        def __init__(self, name, size): pass
        def get_height(self): return 12
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_view.pygame.font.SysFont',
        DummyFont
    )
    screen = SimpleNamespace(get_size=lambda: (100, 100))
    w, h = view._compute_dimensions(screen)
    expected_w = len(view.options) * (THUMB + PAD) + PAD
    expected_h = THUMB + PAD + 12 + PAD
    assert (w, h) == (expected_w, expected_h)


def test_fallback_center_with_view_panel_state(setup_view):
    view = setup_view
    # Provide view_panel_controller state
    vp_state = SimpleNamespace(pos=(10, 20), size=(30, 40))
    view.controller.editor_controller.view_panel_controller = SimpleNamespace(state=vp_state)
    screen = SimpleNamespace(get_size=lambda: (100, 100))
    x, y = view._fallback_center(screen, 20, 40)
    assert (x, y) == (10, 20 + 40 + PAD)


def test_fallback_center_centered(setup_view):
    view = setup_view
    view.controller.editor_controller.view_panel_controller = SimpleNamespace(state=None)
    screen = SimpleNamespace(get_size=lambda: (100, 200))
    w, h = 20, 40
    x, y = view._fallback_center(screen, w, h)
    assert (x, y) == ((100 - w) // 2, (200 - h) // 2)


def test_store_panel_state(setup_view):
    view = setup_view
    ts = view.controller.editor_state.toolbar_state
    view._store_panel_state(5, 6, 7, 8)
    assert ts.collision_picker_panel_size == (7, 8)


def test_render_options_populates_rects(monkeypatch, setup_view):
    view = setup_view
    state = view.state
    # Set up no hover and no selection
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_view.pygame.mouse.get_pos',
        lambda: (0, 0)
    )
    surf = pygame.Surface((200, 200))
    origin_x, origin_y = 5, 5
    view._render_options(surf, origin_x, origin_y)
    rects = state.option_rects
    assert '#' in rects and '.' in rects
    r0 = rects['#']
    assert isinstance(r0, pygame.Rect)
    expected_x0 = origin_x + PAD + 0 * (THUMB + PAD)
    expected_y0 = origin_y + PAD
    assert (r0.x, r0.y, r0.width, r0.height) == (expected_x0, expected_y0, THUMB, THUMB)
    r1 = rects['.']
    expected_x1 = origin_x + PAD + 1 * (THUMB + PAD)
    assert (r1.x, r1.y, r1.width, r1.height) == (expected_x1, expected_y0, THUMB, THUMB)


def test_compute_position_uses_existing(setup_view):
    view = setup_view
    ts = view.controller.editor_state.toolbar_state
    ts.collision_picker_pos = (15, 25)
    pos = view._compute_position(pygame.Surface((100, 100)), 20, 20)
    assert pos == (15, 25)


def test_compute_position_from_icon(setup_view):
    view = setup_view
    ts = view.controller.editor_state.toolbar_state
    ts.collision_picker_pos = None
    icon_rect = pygame.Rect(1, 2, 10, 20)
    toolbar = view.controller.editor_controller.toolbar
    toolbar.icon_rects = {'view_collisions': icon_rect}
    toolbar.padding = 5
    screen = pygame.Surface((100, 100))
    pos = view._compute_position(screen, 30, 40)
    expected_x = icon_rect.right + toolbar.padding
    expected_y = icon_rect.y
    assert pos == (expected_x, expected_y)
    assert ts.collision_picker_pos == (expected_x, expected_y)
