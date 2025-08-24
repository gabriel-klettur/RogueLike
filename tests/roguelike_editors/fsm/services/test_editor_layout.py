import pytest

from roguelike_editors.fsm.services.editor_layout import (
    compute_panel_anchor_next_to_toolbar,
    compute_graph_canvas_anchor,
)


class _StubRect:
    def __init__(self, right: int, top: int) -> None:
        self.right = int(right)
        self.top = int(top)


def test_anchor_next_to_toolbar_basic():
    toolbar_rect = _StubRect(right=10, top=10)
    screen_size = (1280, 720)
    panel_size = (300, 240)

    anchor = compute_panel_anchor_next_to_toolbar(toolbar_rect, screen_size, panel_size)

    # margin=8 by default
    assert anchor == (18, 10)


def test_graph_canvas_anchor_clamped_when_offscreen():
    sets_rect = _StubRect(right=1300, top=700)
    screen_size = (1280, 720)
    canvas_size = (800, 520)

    anchor = compute_graph_canvas_anchor(sets_rect, screen_size, canvas_size=canvas_size)

    # Expected clamp with 4px padding: ax <= 1280-800-4 = 476, ay <= 720-520-4 = 196
    assert anchor == (476, 196)
