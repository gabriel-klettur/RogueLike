import pygame
import pytest

from roguelike_editors.spawner.spawner_instance_properties_panel.visuals.visuals_events import (
    VisualsEvents,
)


class DummyParent:
    def __init__(self):
        self.actions = []

    def get_visuals_rows(self):
        # (state, instance_id, template_id)
        return [("Idle", 1, 2)]

    def open_picker(self, state):
        self.actions.append(("open_picker", state))

    def toggle_building_visibility_for_state(self, state):
        self.actions.append(("toggle_eye", state))

    def clear_visual_for_state(self, state):
        self.actions.append(("clear", state))

    def begin_edit_visual(self, state):
        self.actions.append(("begin_edit", state))

    # Keyboard-related APIs (not used in these tests)
    def get_visual_input_validation(self, state):
        return True, None

    def commit_visual_edit_if_finished(self):
        self.actions.append(("commit_edit", None))

    def cancel_edit_visual(self):
        self.actions.append(("cancel_edit", None))


class DummyVisualsModel:
    def __init__(self):
        self.visuals_browse_rects = []
        self.visuals_eye_rects = []
        self.visuals_clear_rects = []
        self.visuals_template_rects = []
        self.visuals_row_rects = []
        self.hold_active = False
        self.hold_row_index = None


class DummyVisualsController:
    def __init__(self):
        self.parent = DummyParent()
        self.model = DummyVisualsModel()

    # Bridge APIs used by events
    def open_picker(self, state):
        self.parent.open_picker(state)

    def toggle_building_visibility_for_state(self, state):
        self.parent.toggle_building_visibility_for_state(state)

    def clear_visual_for_state(self, state):
        self.parent.clear_visual_for_state(state)

    def begin_edit_visual(self, state):
        self.parent.begin_edit_visual(state)

    def center_camera_on_state(self, state):
        # no-op for tests
        pass


@pytest.fixture(autouse=True)
def _init_pygame():
    # Pygame is configured for dummy video driver in tests/conftest.py
    if not pygame.get_init():
        pygame.init()
    yield
    pygame.event.clear()


def _panel_and_rects():
    panel = pygame.Rect(100, 100, 420, 360)
    # Place the first visuals row around y = 150 (panel-local)
    row_y = 150
    template_rect = pygame.Rect(310, row_y - 1, 100, 18)
    browse_rect = pygame.Rect(template_rect.right - 18, template_rect.y + 2, 16, template_rect.height - 4)
    eye_rect = pygame.Rect(browse_rect.left - 18, template_rect.y + 2, 16, template_rect.height - 4)
    clear_rect = pygame.Rect(eye_rect.left - 18, template_rect.y + 2, 16, template_rect.height - 4)
    row_rect = pygame.Rect(8, row_y - 1, panel.width - 16, 18)
    state_rect = pygame.Rect(10, row_y - 1, 190, 18)
    return panel, template_rect, browse_rect, eye_rect, clear_rect, row_rect, state_rect


def test_browse_click_opens_picker():
    vctrl = DummyVisualsController()
    panel, template_rect, browse_rect, eye_rect, clear_rect, row_rect, state_rect = _panel_and_rects()
    m = vctrl.model
    m.visuals_template_rects.append(template_rect)
    m.visuals_browse_rects.append(browse_rect)
    m.visuals_eye_rects.append(eye_rect)
    m.visuals_clear_rects.append(clear_rect)
    m.visuals_row_rects.append(row_rect)

    ev = VisualsEvents()
    # Click inside browse rect (translate to screen coords)
    click_pos = (panel.left + browse_rect.centerx, panel.top + browse_rect.centery)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": click_pos, "button": 1})
    handled = ev.handle_event(vctrl, event, panel)

    assert handled is True
    assert ("open_picker", "Idle") in vctrl.parent.actions


def test_eye_click_toggles_visibility():
    vctrl = DummyVisualsController()
    panel, template_rect, browse_rect, eye_rect, clear_rect, row_rect, state_rect = _panel_and_rects()
    m = vctrl.model
    m.visuals_template_rects.append(template_rect)
    m.visuals_browse_rects.append(browse_rect)
    m.visuals_eye_rects.append(eye_rect)
    m.visuals_clear_rects.append(clear_rect)
    m.visuals_row_rects.append(row_rect)

    ev = VisualsEvents()
    click_pos = (panel.left + eye_rect.centerx, panel.top + eye_rect.centery)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": click_pos, "button": 1})
    handled = ev.handle_event(vctrl, event, panel)

    assert handled is True
    assert ("toggle_eye", "Idle") in vctrl.parent.actions


def test_clear_click_clears_visual():
    vctrl = DummyVisualsController()
    panel, template_rect, browse_rect, eye_rect, clear_rect, row_rect, state_rect = _panel_and_rects()
    m = vctrl.model
    m.visuals_template_rects.append(template_rect)
    m.visuals_browse_rects.append(browse_rect)
    m.visuals_eye_rects.append(eye_rect)
    m.visuals_clear_rects.append(clear_rect)
    m.visuals_row_rects.append(row_rect)

    ev = VisualsEvents()
    click_pos = (panel.left + clear_rect.centerx, panel.top + clear_rect.centery)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": click_pos, "button": 1})
    handled = ev.handle_event(vctrl, event, panel)

    assert handled is True
    assert ("clear", "Idle") in vctrl.parent.actions


def test_template_cell_begins_edit():
    vctrl = DummyVisualsController()
    panel, template_rect, browse_rect, eye_rect, clear_rect, row_rect, state_rect = _panel_and_rects()
    m = vctrl.model
    m.visuals_template_rects.append(template_rect)
    m.visuals_browse_rects.append(browse_rect)
    m.visuals_eye_rects.append(eye_rect)
    m.visuals_clear_rects.append(clear_rect)
    m.visuals_row_rects.append(row_rect)

    ev = VisualsEvents()
    click_pos = (panel.left + template_rect.centerx, panel.top + template_rect.centery)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": click_pos, "button": 1})
    handled = ev.handle_event(vctrl, event, panel)

    assert handled is True
    assert ("begin_edit", "Idle") in vctrl.parent.actions
