import pygame

from roguelike_ui.widgets.grid import GridLayout, ScrollableGrid


def test_gridlayout_compute_dimensions():
    gl = GridLayout(thumb_size=16, pad=4, items_count=10, cols=4)
    cols, rows, w, h = gl.compute()
    assert cols == 4 and rows == 3
    # width = cols*(thumb+pad)+pad = 4*(16+4)+4 = 4*20+4 = 84
    assert w == 84
    # height = rows*(thumb+pad)+pad = 3*(16+4)+4 = 64
    assert h == 64


def test_scrollablegrid_draw_items_and_hover(monkeypatch):
    sg = ScrollableGrid(thumb_size=10, pad=2, items_count=9, scroll_offset=0, cols=3)

    # Fake mouse position over the second item in first row
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (2 + (10+2) + 1, 2 + 5), raising=True)

    # Surface large enough
    surf = pygame.Surface((200, 200))

    drawn = []
    def draw_fn(surface, rect, value, index):
        drawn.append((value, index, rect))

    items = list(range(9))
    hovered = sg.draw_items(surf, items, panel_pos=(0, 0), draw_fn=draw_fn)

    # All 9 items within viewport should be drawn for zero scroll
    assert len(drawn) == 9
    # Hovered should be value at index 1
    assert hovered == 1
