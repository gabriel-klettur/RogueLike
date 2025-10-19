import pygame

from roguelike_ui.widgets.list_panel_ui import ListPanelUI


def test_list_panel_ui_scroll_and_outside_click(monkeypatch):
    font = pygame.font.SysFont(None, 14)
    ui = ListPanelUI(font)
    # Many items to enable scrolling
    items = [f"item-{i}" for i in range(30)]
    ui.set_items(items)

    surface = pygame.Surface((160, 90), flags=pygame.SRCALPHA)
    rect = pygame.Rect(10, 5, 120, 60)

    # Initial draw
    ui.draw(surface, rect)

    # MOUSEWHEEL scroll down requires mouse.get_pos() inside panel rect
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (rect.x + 10, rect.y + 10), raising=True)
    ev_wheel_down = pygame.event.Event(pygame.MOUSEWHEEL, y=-1)
    handled = ui.handle_event(ev_wheel_down)
    assert handled is True
    # Scroll offset should be > 0 after wheel down
    assert ui.panel.scroll_offset >= font.get_linesize()

    # Click outside rect should not select
    outside_pos = (rect.right + 5, rect.bottom + 5)
    ev_out = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=outside_pos)
    handled2 = ui.handle_event(ev_out)
    assert handled2 is False
    assert ui.selected_index is None

    # Click inside after scroll: target a lower item index (e.g., 5)
    ui.draw(surface, rect)
    line_h = font.get_linesize()
    click_inside = (rect.x + 5, rect.y + 5 + 5 * line_h)
    ev_in = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=click_inside)
    handled3 = ui.handle_event(ev_in)
    assert handled3 is True
    assert ui.selected_index is not None
