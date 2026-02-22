import pygame

from roguelike_ui.widgets.toolbar_panel import ToolbarView


def make_icon(size=8):
    s = pygame.Surface((size, size), flags=pygame.SRCALPHA)
    s.fill((100, 100, 100, 255))
    return s


def test_toolbar_view_with_empty_items_and_controller_without_is_active(monkeypatch):
    # Controller without is_active; blink_active may also be absent
    class C:  # minimal
        pass
    ctrl = C()

    tv = ToolbarView(controller=ctrl, items=[], icons={}, x=0, y=0, size=12, padding=2)

    screen = pygame.Surface((100, 60), flags=pygame.SRCALPHA)
    # Mouse far away to avoid hover computations issues
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (1000, 1000), raising=True)

    # Render should not crash with empty items
    tv.render(screen)

    # handle_event with left click should return False (drag uses right click)
    ev_left = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(1, 1))
    assert tv.handle_event(ev_left) is False


def test_toolbar_view_controller_active_and_blink_exceptions_are_ignored(monkeypatch):
    class BadController:
        def is_active(self, tool: str) -> bool:
            # Raise to exercise defensive try/except in view
            raise RuntimeError("boom")
        def blink_active(self, tool: str) -> bool:
            raise RuntimeError("boom2")

    tools = ["t1", "t2"]
    icons = {t: make_icon(6) for t in tools}
    tv = ToolbarView(controller=BadController(), items=tools, icons=icons, x=2, y=2, size=10, padding=2)

    screen = pygame.Surface((120, 80), flags=pygame.SRCALPHA)
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (1000, 1000), raising=True)

    # Should not raise despite controller exceptions
    tv.render(screen)

    # Right-click drag outside header -> no drag
    ev_right_out = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(0, 0))
    assert tv.handle_event(ev_right_out) is False
