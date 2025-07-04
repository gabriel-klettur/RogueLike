import os
import pygame
import pytest

from roguelike_editors.items.model.editor_model import ItemEditorModel
from roguelike_editors.items.controller.editor_controller import ItemEditorController
from roguelike_editors.items.view.editor_view import ItemEditorView

class DummyItem:
    def __init__(self, name, description):
        self.name = name
        self.description = description

@pytest.fixture(scope="module", autouse=True)
def init_pygame():
    os.environ["SDL_VIDEODRIVER"] = "dummy"
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_model_initial_state():
    model = ItemEditorModel(items={"a": DummyItem("A", "Desc A")}, assets={})
    assert not model.visible
    assert model.scroll_index == 0
    assert model.hovered_item_id is None


def test_toggle_visibility_and_scroll():
    items = {"a": DummyItem("A", ""), "b": DummyItem("B", ""), "c": DummyItem("C", "")}
    font = pygame.font.SysFont(None, 24)
    controller = ItemEditorController(items, {}, font)
    model = controller.model
    # Toggle on
    ev_f7 = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_F7})
    controller.handle_event(ev_f7)
    assert model.visible
    # Scroll down
    ev_down = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_DOWN})
    controller.handle_event(ev_down)
    assert model.scroll_index == 1
    controller.handle_event(ev_down)
    assert model.scroll_index == 2
    controller.handle_event(ev_down)
    assert model.scroll_index == 2
    # Scroll up
    ev_up = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_UP})
    controller.handle_event(ev_up)
    assert model.scroll_index == 1
    controller.handle_event(ev_up)
    assert model.scroll_index == 0
    controller.handle_event(ev_up)
    assert model.scroll_index == 0
    # Toggle off
    controller.handle_event(ev_f7)
    assert not model.visible


def test_hover_detection():
    items = {"x": DummyItem("X", "Desc X")}
    font = pygame.font.SysFont(None, 24)
    controller = ItemEditorController(items, {}, font)
    model = controller.model
    # Show editor
    controller.handle_event(pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_F7}))
    # Inside first cell
    margin = 20
    pos_inside = (margin + 10, margin + 10)
    ev_motion = pygame.event.Event(pygame.MOUSEMOTION, {"pos": pos_inside})
    controller.handle_event(ev_motion)
    assert model.hovered_item_id == "x"
    # Outside cell
    ev_motion2 = pygame.event.Event(pygame.MOUSEMOTION, {"pos": (0, 0)})
    controller.handle_event(ev_motion2)
    assert model.hovered_item_id is None


def test_wrap_text():
    font = pygame.font.SysFont(None, 24)
    view = ItemEditorView({}, font)
    text = "one two three"
    # Set max width smaller than two words
    max_w = view.font.size("one two")[0] - 10
    lines = view._wrap_text(text, max_w)
    assert len(lines) >= 2
    assert all(isinstance(l, str) for l in lines)
