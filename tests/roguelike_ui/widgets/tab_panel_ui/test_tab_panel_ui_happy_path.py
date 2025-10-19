import pygame

from roguelike_ui.widgets.tab_panel_ui import TabPanelUI


def test_tab_panel_ui_draw_and_click_select():
    font = pygame.font.SysFont(None, 14)
    ui = TabPanelUI(font, padding=6)

    surface = pygame.Surface((240, 80), flags=pygame.SRCALPHA)
    tabs = ["Home", "Edit", "View"]

    # Initial draw with 'Home' selected
    ui.draw(surface, x=10, y=10, tabs=tabs, selected="Home")

    # Click inside the second tab ('Edit') rect
    rects = ui.tab_rects
    assert len(rects) == 3
    edit_rect, _ = rects[1]
    click_pos = (edit_rect.centerx, edit_rect.centery)

    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=click_pos)
    selected = ui.handle_event(ev)

    assert selected == "Edit"
