import pygame

from roguelike_ui.widgets.toolbar_panel import ToolbarView


class DummyController:
    def __init__(self, active):
        self._active = set(active)
    def is_active(self, tool: str) -> bool:
        return tool in self._active
    def blink_active(self, tool: str) -> bool:
        # deterministic: blink only on a specific tool
        return tool == next(iter(self._active)) if self._active else False


def make_icon(size=16):
    surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
    surf.fill((20, 20, 20, 255))
    return surf


def test_toolbar_view_render_and_drag(monkeypatch):
    tools = ["select", "paint", "erase"]
    icons = {t: make_icon(12) for t in tools}
    ctrl = DummyController(active={"paint"})

    tv = ToolbarView(controller=ctrl, items=tools, icons=icons, x=10, y=10, size=16, padding=4)

    screen = pygame.Surface((200, 200), flags=pygame.SRCALPHA)

    # Render once (hover depends on mouse; keep it far)
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (1000, 1000), raising=True)
    tv.render(screen)

    # Simulate right-click drag on panel header
    header_pos = (tv.panel.pos[0] + 1, tv.panel.pos[1] + 1)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=header_pos)
    ev_motion = pygame.event.Event(pygame.MOUSEMOTION, pos=(header_pos[0] + 5, header_pos[1] + 7))
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, button=3, pos=(header_pos[0] + 5, header_pos[1] + 7))

    tv.handle_event(ev_down)
    tv.handle_event(ev_motion)
    tv.handle_event(ev_up)

    # Render again with mouse over first button to exercise hover overlay
    bx, by = tv.panel.pos or (10, 10)
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (bx + 10, by + 12), raising=True)
    tv.render(screen)

    # Basic sanity: icon rects tracked and selection border can be drawn
    assert set(tv.icon_rects.keys()) == set(tools)
