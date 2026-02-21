import pygame

from roguelike_ui.widgets.tab_panel_ui import TabPanelUI


def test_tab_panel_ui_click_outside_and_empty_tabs():
    font = pygame.font.SysFont(None, 14)
    ui = TabPanelUI(font, padding=4)
    surface = pygame.Surface((200, 60), flags=pygame.SRCALPHA)

    # Draw with some tabs, but click outside any rect -> None
    tabs = ["A", "B"]
    ui.draw(surface, x=10, y=10, tabs=tabs, selected="Z")  # selected not in tabs
    # Click far outside
    ev_out = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(180, 55))
    assert ui.handle_event(ev_out) is None

    # Draw with empty tabs -> no rects, no selection possible
    ui.draw(surface, x=10, y=10, tabs=[], selected="")
    assert ui.tab_rects == []
    ev_any = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(12, 12))
    assert ui.handle_event(ev_any) is None
