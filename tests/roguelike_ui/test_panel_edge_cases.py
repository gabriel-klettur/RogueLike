import pygame

from roguelike_ui.panel import PanelSurface, DraggablePanel


def test_panelsurface_resize_same_size_and_sample_fill():
    p = PanelSurface(32, 24, bgcolor=(1, 2, 3, 128))
    # Resize to same size should preserve size and still be filled
    p.resize(32, 24)
    assert p.surface.get_size() == (32, 24)
    assert tuple(p.surface.get_at((0, 0))) == (1, 2, 3, 128)


def test_draggablepanel_ignores_left_click_and_outside_header():
    dp = DraggablePanel(20, 10)
    dp.pos = (5, 5)
    header = pygame.Rect(dp.pos, dp.surface.get_size())

    # Left click inside header does not start dragging
    ev_left = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(6, 6))
    assert dp.handle_event(ev_left, header_rect=header) is False
    assert dp.dragging is False

    # Right click outside header does not start dragging
    ev_right_out = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(0, 0))
    assert dp.handle_event(ev_right_out, header_rect=header) is False
    assert dp.dragging is False

    # Motion when not dragging returns False and does not change pos
    old_pos = dp.pos
    ev_motion = pygame.event.Event(pygame.MOUSEMOTION, pos=(50, 50))
    assert dp.handle_event(ev_motion, header_rect=header) is False
    assert dp.pos == old_pos

    # Mouse up when not dragging returns False
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, button=3, pos=(6, 6))
    assert dp.handle_event(ev_up, header_rect=header) is False

    # None header_rect does not crash and returns False
    ev_right_none = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(6, 6))
    assert dp.handle_event(ev_right_none, header_rect=None) is False
