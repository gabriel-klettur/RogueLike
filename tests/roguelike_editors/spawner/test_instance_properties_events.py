import pygame
import pytest

from roguelike_editors.spawner.spawner_instance_properties_panel.instance_properties_events import (
    InstancePropertiesEventHandler,
)


class DummyModel:
    def __init__(self):
        self.visible = True
        self.scroll_offset = 0
        self.template_combo_open = False
        self.template_scroll_offset = 0
        self.template_hovered_index = None
        self.editing_key = None
        self.editing_row_index = None


class DummyView:
    def __init__(self, panel_rect, combo_rect):
        self.panel_rect = panel_rect
        self.template_combo_rect = combo_rect
        self.template_list_rect = None
        self.content_height = 360


class DummyDoubleClick:
    def is_double_click(self, key):
        return False


class DummyController:
    def __init__(self):
        panel = pygame.Rect(100, 100, 440, 360)
        combo = pygame.Rect(310, 150, 210, 18)
        self.model = DummyModel()
        self.view = DummyView(panel, combo)
        self._dbl = DummyDoubleClick()
        self._options = ["A", "B", "C", "D"]
        self._current_idx = 0
        self._rows = [("template_id", self._options[self._current_idx]), ("other", 123)]

    def get_rows(self):
        return list(self._rows)

    def is_editing(self):
        return False

    def get_text_input(self):
        return None

    def get_template_options(self):
        return list(self._options)

    def get_current_template_index(self):
        return int(self._current_idx)

    def select_template_by_index(self, idx):
        if 0 <= idx < len(self._options):
            self._current_idx = idx
            self._rows[0] = ("template_id", self._options[idx])


@pytest.fixture(autouse=True)
def _init_pygame():
    if not pygame.get_init():
        pygame.init()
    yield
    pygame.event.clear()


def test_combo_toggle_and_select_item():
    c = DummyController()
    h = InstancePropertiesEventHandler()
    m = c.model
    v = c.view

    # Click inside the combo box to open it
    pos_open = (v.panel_rect.left + v.template_combo_rect.centerx,
                v.panel_rect.top + v.template_combo_rect.centery)
    ev_open = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": pos_open, "button": 1})
    handled = h.handle_event(c, ev_open)
    assert handled is True
    assert m.template_combo_open is True
    # When opening, hovered index should be current selection
    assert m.template_hovered_index == c.get_current_template_index()

    # Prepare dropdown list rect (8 visible rows max, we have 4)
    row_h = 20
    visible_rows = min(8, len(c.get_template_options()))
    list_h = visible_rows * row_h
    v.template_list_rect = pygame.Rect(
        v.template_combo_rect.x,
        v.template_combo_rect.bottom + 2,
        v.template_combo_rect.width,
        list_h,
    )

    # Click on the 2nd item (index 1)
    target_idx = 1
    m.template_scroll_offset = 0
    click_y = v.template_list_rect.y + (target_idx - m.template_scroll_offset) * row_h + row_h // 2
    click_x = v.template_list_rect.x + 10
    pos_select = (v.panel_rect.left + click_x, v.panel_rect.top + click_y)
    ev_sel = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": pos_select, "button": 1})
    handled2 = h.handle_event(c, ev_sel)

    assert handled2 is True
    assert c.get_current_template_index() == 1
    assert c.get_rows()[0][1] == "B"
    assert m.template_combo_open is False
    assert m.template_hovered_index is None
