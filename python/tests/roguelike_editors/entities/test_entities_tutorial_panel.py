import pygame
import pytest

from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_events import (
    EntitiesToolBarPanelEventHandler,
)
from roguelike_editors.entities.entities_tutorial_panel.entities_tutorial_panel_events import (
    EntitiesTutorialPanelEventHandler,
)
from roguelike_ui.panel import DraggablePanel


@pytest.fixture(autouse=True)
def _pygame_init_teardown():
    pygame.init()
    try:
        yield
    finally:
        pygame.quit()


def _lmb(pos):
    return pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": pos, "button": 1})


def _rmb_down(pos):
    return pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": pos, "button": 3})


def _rmb_up(pos):
    return pygame.event.Event(pygame.MOUSEBUTTONUP, {"pos": pos, "button": 3})


def _motion(pos):
    return pygame.event.Event(pygame.MOUSEMOTION, {"pos": pos, "rel": (0, 0), "buttons": (0, 0, 1)})


class _ToolbarWidgetStub:
    def __init__(self, icon_rects):
        self.icon_rects = icon_rects


class _ToolbarViewStub:
    def __init__(self, icon_rects):
        self.widget = _ToolbarWidgetStub(icon_rects)


class _TutorialControllerStub:
    def __init__(self):
        self.activated = False
        self.deactivated = False

    def activate(self):
        self.activated = True

    def deactivate(self):
        self.deactivated = True


class _ToolbarControllerStub:
    def __init__(self, icon_rects):
        self.toolbar_view = _ToolbarViewStub(icon_rects)
        self.tutorial_controller = _TutorialControllerStub()
        # Attributes used by other toolbar actions but not relevant here
        self.history = type("H", (), {"undo": lambda self: False, "redo": lambda self: False})()
        self.model = type("M", (), {})()


def test_tutorial_button_toggles_panel_active_state():
    # Arrange controller with an icon rect for 'tutorial_entities'
    icon_rect = pygame.Rect(10, 10, 24, 24)
    controller = _ToolbarControllerStub({"tutorial_entities": icon_rect})
    model = type("ToolbarModel", (), {"active_tool": None})()
    handler = EntitiesToolBarPanelEventHandler(controller, model)

    # Click inside the tutorial icon: should activate
    consumed = handler.handle_event(_lmb(icon_rect.center))
    assert consumed is True
    assert model.active_tool == "tutorial_entities"
    assert controller.tutorial_controller.activated is True

    # Click again: should deactivate
    consumed = handler.handle_event(_lmb(icon_rect.center))
    assert consumed is True
    assert model.active_tool is None
    assert controller.tutorial_controller.deactivated is True


def test_tutorial_panel_is_draggable_via_right_click_on_header():
    # Model active with header and panel rects
    model = type(
        "TutorialModel",
        (),
        {
            "active": True,
            # header area where dragging is allowed
            "header_rect": pygame.Rect(100, 100, 300, 40),
        },
    )()

    # View with a real DraggablePanel and initial position set
    view = type("V", (), {})()
    view.panel = DraggablePanel(520, 200)
    view.panel.pos = (100, 100)

    # Controller stub wiring
    controller = type("C", (), {"view": view})()

    # Events handler delegates to DraggablePanel
    events = EntitiesTutorialPanelEventHandler(controller, model)

    # Start dragging with right button on header
    assert events.handle(_rmb_down((110, 110))) is True
    # Move some pixels
    assert events.handle(_motion((160, 140))) is True
    # Release
    assert events.handle(_rmb_up((160, 140))) is True

    # Panel should have moved roughly by the motion delta (accounting for initial offset)
    x, y = view.panel.pos
    assert x != 100 or y != 100
