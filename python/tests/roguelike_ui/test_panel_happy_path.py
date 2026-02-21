import pygame

from roguelike_ui.panel import PanelSurface, DraggablePanel


def test_panelsurface_init_and_resize_fills_background():
    # Initial surface has expected size and is filled
    p = PanelSurface(40, 30, bgcolor=(10, 20, 30, 200))
    assert p.surface.get_size() == (40, 30)

    # Sample a pixel to ensure fill applied
    px = p.surface.get_at((0, 0))
    assert tuple(px) == (10, 20, 30, 200)

    # Resize should recreate surface and re-fill with same bg color
    p.resize(64, 16)
    assert p.surface.get_size() == (64, 16)
    px2 = p.surface.get_at((63, 15))
    assert tuple(px2) == (10, 20, 30, 200)


def test_draggablepanel_drag_sequence():
    dp = DraggablePanel(20, 10)
    dp.pos = (5, 5)
    header = pygame.Rect(dp.pos, dp.surface.get_size())

    # Start dragging with right click inside header
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(6, 6))
    assert dp.handle_event(ev_down, header_rect=header) is True
    assert dp.dragging is True

    # Move mouse: updates position
    ev_motion = pygame.event.Event(pygame.MOUSEMOTION, pos=(16, 18))
    assert dp.handle_event(ev_motion, header_rect=header) is True
    assert isinstance(dp.pos, tuple)

    # Finish dragging with right button up
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, button=3, pos=(16, 18))
    assert dp.handle_event(ev_up, header_rect=header) is True
    assert dp.dragging is False
