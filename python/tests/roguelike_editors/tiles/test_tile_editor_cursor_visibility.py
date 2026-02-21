import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tile_editor_view import TileEditorView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.display.set_mode((1, 1))
    yield
    pygame.quit()

@pytest.fixture
def setup_view():
    """Return a TileEditorView with dummy controller and editor_state."""
    editor_state = SimpleNamespace(
        active=True,
        picker_state=SimpleNamespace(open=False),
        size_panel_state=SimpleNamespace(visible=False, selected_size=(1, 1)),
        toolbar_state=SimpleNamespace(view_active=False, layers_view_open=False, collision_picker_open=False),
        current_tool=None,
        current_layer=SimpleNamespace(value=0, name="")
    )
    # Dummy controller with stub methods
    controller = SimpleNamespace()
    controller._tile_under_mouse = lambda pos, camera, map: None
    controller.title_controller = SimpleNamespace(render=lambda screen: None)
    controller.toolbar = SimpleNamespace(view=SimpleNamespace(render=lambda screen: None))
    controller.size_panel_controller = SimpleNamespace(render=lambda screen: None)
    controller.picker = SimpleNamespace(view=SimpleNamespace(render=lambda screen: None))
    controller.layers_panel_controller = SimpleNamespace(render=lambda screen: None)
    controller.collision_panel_controller = SimpleNamespace(render=lambda screen: None)
    controller.view_panel_controller = SimpleNamespace(render=lambda screen, camera, map: None)
    controller.outline_view = SimpleNamespace(render=lambda screen, camera, map: None)
    view = TileEditorView(controller, editor_state)
    return view, controller, editor_state


def test_picker_open_shows_cursor_and_renders_picker(monkeypatch, setup_view):
    view, controller, editor = setup_view
    # Record picker.render calls
    rendered = []
    controller.picker.view.render = lambda screen: rendered.append(True)
    # Record cursor visibility changes
    vis = []
    monkeypatch.setattr(pygame.mouse, 'set_visible', lambda v: vis.append(v))
    # Simulate renderer
    screen = pygame.Surface((10, 10))
    camera = SimpleNamespace(apply=lambda p: p)
    # Open picker
    editor.picker_state.open = True
    view.render(screen, camera, None)
    assert vis and vis[-1] is True, "Cursor should be visible when picker is open"
    assert rendered, "Picker view.render should be called when picker is open"


def test_brush_over_map_hides_cursor_when_no_panels(monkeypatch, setup_view):
    view, controller, editor = setup_view
    # Simulate mouse position
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (0, 0))
    vis = []
    monkeypatch.setattr(pygame.mouse, 'set_visible', lambda v: vis.append(v))
    # Set brush mode and tile under mouse
    editor.current_tool = 'brush'
    controller._tile_under_mouse = lambda pos, camera, map: SimpleNamespace(x=1, y=1)
    view.render(pygame.Surface((10,10)), SimpleNamespace(apply=lambda p: p), None)
    assert vis and vis[-1] is False, "Cursor should be hidden when brush over map and no panels are open"


def test_brush_over_map_shows_cursor_when_panel_open(monkeypatch, setup_view):
    view, controller, editor = setup_view
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (0, 0))
    vis = []
    monkeypatch.setattr(pygame.mouse, 'set_visible', lambda v: vis.append(v))
    editor.current_tool = 'brush'
    controller._tile_under_mouse = lambda pos, camera, map: SimpleNamespace(x=1, y=1)
    # Emulate a panel open
    editor.toolbar_state.view_active = True
    view.render(pygame.Surface((10,10)), SimpleNamespace(apply=lambda p: p), None)
    assert vis and vis[-1] is True, "Cursor should be visible when brush over map but a panel is open"


def test_brush_not_over_map_shows_cursor(monkeypatch, setup_view):
    view, controller, editor = setup_view
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (0, 0))
    vis = []
    monkeypatch.setattr(pygame.mouse, 'set_visible', lambda v: vis.append(v))
    editor.current_tool = 'brush'
    controller._tile_under_mouse = lambda pos, camera, map: None
    view.render(pygame.Surface((10,10)), SimpleNamespace(apply=lambda p: p), None)
    assert vis and vis[-1] is True, "Cursor should be visible when brush not over map"


def test_non_brush_shows_cursor(monkeypatch, setup_view):
    view, controller, editor = setup_view
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (0, 0))
    vis = []
    monkeypatch.setattr(pygame.mouse, 'set_visible', lambda v: vis.append(v))
    editor.current_tool = 'erase'
    view.render(pygame.Surface((10,10)), SimpleNamespace(apply=lambda p: p), None)
    assert vis and vis[-1] is True, "Cursor should be visible for non-brush tools"
