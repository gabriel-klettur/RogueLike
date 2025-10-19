import pygame

from roguelike_ui.widgets.list_panel_ui import ListPanelUI


def test_list_panel_ui_draw_and_click_select(monkeypatch):
    font = pygame.font.SysFont(None, 14)
    ui = ListPanelUI(font)
    items = ["one", "two", "three", "four"]
    ui.set_items(items)

    surface = pygame.Surface((120, 80), flags=pygame.SRCALPHA)
    rect = pygame.Rect(10, 5, 100, 60)

    # First draw populates rect and renders items
    ui.draw(surface, rect)

    # Click on the second line (index 1)
    line_h = font.get_linesize()
    click_pos = (rect.x + 5, rect.y + 1 * line_h + line_h // 2)
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=click_pos)

    handled = ui.handle_event(ev)
    assert handled is True
    assert ui.selected_index == 1

    # Next draw should highlight the selected line (no crash expected)
    ui.draw(surface, rect)
