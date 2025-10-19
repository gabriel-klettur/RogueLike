import pygame

from roguelike_ui.widgets.grid import ScrollableGrid


def test_scrollablegrid_skips_items_outside_viewport(monkeypatch):
    # 5x5 items, thumb 10, pad 2 -> grid height = rows*(10+2)+2 = 5*12+2 = 62
    sg = ScrollableGrid(thumb_size=10, pad=2, items_count=25, scroll_offset=0, cols=5)

    # Mouse far away to avoid hover
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (1000, 1000), raising=True)

    surf = pygame.Surface((200, 200))

    drawn = []
    def draw_fn(surface, rect, value, index):
        drawn.append((value, index))

    items = list(range(25))
    hovered = sg.draw_items(surf, items, panel_pos=(0, 0), draw_fn=draw_fn)

    assert hovered is None
    assert len(drawn) == 25  # no scroll: all within viewport

    # With scroll, early rows should be skipped
    sg.scroll_offset = 20  # pushes first row above viewport
    drawn2 = []
    hovered2 = sg.draw_items(surf, items, panel_pos=(0, 0), draw_fn=lambda s, r, v, i: drawn2.append((v,i)))

    assert hovered2 is None
    assert 0 < len(drawn2) < len(drawn)
