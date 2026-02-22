import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.size_panel.size_panel_view import SizePanelView
from roguelike_editors.tiles.size_panel.size_panel_state import SizePanelState

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def make_dummy_controller():
    toolbar = SimpleNamespace(x=10, y=20, size=30, padding=5)
    return SimpleNamespace(editor_controller=SimpleNamespace(toolbar=toolbar), editor_state=None)


def test_ensure_panel_position_initializes_pos_and_panel_pos():
    state = SizePanelState()
    controller = make_dummy_controller()
    view = SizePanelView(controller, state)
    assert state.pos is None
    view._ensure_panel_position()
    expected = (controller.editor_controller.toolbar.x + controller.editor_controller.toolbar.size + controller.editor_controller.toolbar.padding,
                controller.editor_controller.toolbar.y)
    assert state.pos == expected
    assert view.panel.pos == expected


def test_render_does_nothing_when_not_visible():
    state = SizePanelState()
    state.option_rects = {42: pygame.Rect(0, 0, 1, 1)}
    controller = make_dummy_controller()
    view = SizePanelView(controller, state)
    screen = pygame.Surface((100, 100))
    state.visible = False
    view.render(screen)
    # option_rects should remain unchanged
    assert state.option_rects == {42: pygame.Rect(0, 0, 1, 1)}


def test_render_populates_option_rects_when_visible():
    state = SizePanelState()
    controller = make_dummy_controller()
    view = SizePanelView(controller, state)
    screen = pygame.Surface((200, 200))
    state.visible = True
    # ensure state.pos set
    view._ensure_panel_position()
    # add dummy before render
    state.option_rects = {}
    view.render(screen)
    # after render, should have one rect per size
    assert set(state.option_rects.keys()) == set(range(len(state.sizes)))
    for rect in state.option_rects.values():
        assert isinstance(rect, pygame.Rect)
