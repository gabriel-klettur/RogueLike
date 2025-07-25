import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_view import TileToolbarView
from roguelike_editors.tiles.tiles_editor_config import TOOLS, BTN_W, BTN_H, THUMB, PAD, CLR_SELECTION, CLR_HOVER

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_view(monkeypatch):
    # Dummy controller
    ctrl = SimpleNamespace(
        x=5,
        y=7,
        size=16,
        padding=2,
        icons={tool: pygame.Surface((16, 16)) for tool in TOOLS},
        icon_rects={}
    )
    # Dummy state
    state = SimpleNamespace(toolbar_state=SimpleNamespace(pos=None, view_active=False, layers_view_open=False, show_collisions=False, show_collisions_overlay=False), current_tool=None)
    ctrl.editor_state = state
    # Monkeypatch panel and button
    class DummyPanel:
        def __init__(self, width, height): self.surface = pygame.Surface((width, height))
        def resize(self, w, h): self.surface = pygame.Surface((w, h))
    monkeypatch.setattr('roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_view.DraggablePanel', DummyPanel)

    # Monkeypatch button
    class DummyButton:
        def __init__(self, rect, bgcolor, border_color, hover_color): self.rect=rect; self.hover=False
        def is_hovered(self, rel): self.hover = True
        def draw(self, surf): pass
    monkeypatch.setattr('roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_view.Button', DummyButton)
    view = TileToolbarView(ctrl)

    return view, ctrl


def test_init_properties(setup_view):
    view, ctrl = setup_view
    assert view.toolbar is ctrl
    assert hasattr(view, 'buttons')
    assert isinstance(view.buttons, dict)


def test_compute_icon_rect(setup_view):
    view, ctrl = setup_view
    rect = view._compute_icon_rect(1, 2, 3)
    assert isinstance(rect, pygame.Rect)
    assert rect.topleft == (1, 2 + 3 * (ctrl.size + ctrl.padding))


def test_get_panel_position_from_state(setup_view):
    view, ctrl = setup_view
    # Without state.pos
    assert view._get_panel_position() == (ctrl.x, ctrl.y)


def test_render_draws_panel_and_updates_icon_rects(setup_view):
    view, ctrl = setup_view
    screen = pygame.Surface((100, 100))
    # Render should populate icon_rects
    view.render(screen)
    for tool in TOOLS:
        assert tool in ctrl.icon_rects
    # Ensure panel blitted
    # Pixel on panel surface area should not be default (0) after blit
    assert screen.get_at(ctrl.icon_rects[TOOLS[0]].topleft) != pygame.Color(0,0,0,0)
