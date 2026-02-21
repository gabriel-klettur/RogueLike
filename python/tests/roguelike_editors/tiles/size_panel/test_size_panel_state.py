import pytest
import pygame
from roguelike_editors.tiles.size_panel.size_panel_state import SizePanelState


def test_default_state():
    state = SizePanelState()
    assert len(state.sizes) == 10
    assert state.selected_index == 0
    assert state.selected_size == (1, 1)
    assert state.visible is False
    assert state.option_rects == {}
    assert state.pos is None
    assert state.dragging is False
    assert state.drag_offset == (0, 0)


def test_select_valid_index():
    state = SizePanelState()
    state.select(5)
    assert state.selected_index == 5
    assert state.selected_size == state.sizes[5]


def test_select_invalid_index():
    state = SizePanelState()
    prev = state.selected_index
    state.select(-1)
    assert state.selected_index == prev
    state.select(len(state.sizes))
    assert state.selected_index == prev
