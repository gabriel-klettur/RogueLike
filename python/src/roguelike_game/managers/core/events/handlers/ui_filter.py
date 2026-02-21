import pygame
from roguelike_ui.ui_blocker import is_blocked
from ..utils import allow_mmb_ui as _allow_mmb_ui, is_mmb_held as _is_mmb_held

def build_remaining_events(game, events, consumed_idx: set):
    blocked_idx: set[int] = set()
    for i, ev in enumerate(events):
        if i in consumed_idx:
            continue
        if ev.type == pygame.MOUSEWHEEL:
            mx, my = pygame.mouse.get_pos()
            if is_blocked(mx, my):
                blocked_idx.add(i)
        elif ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            btn = getattr(ev, 'button', None)
            allow_mmb = _allow_mmb_ui(game)
            if btn == 2 and allow_mmb:
                continue
            mx, my = getattr(ev, 'pos', (None, None))
            if mx is not None and is_blocked(mx, my):
                blocked_idx.add(i)
        elif ev.type == pygame.MOUSEMOTION:
            mx, my = getattr(ev, 'pos', (None, None))
            if mx is None:
                continue
            try:
                mmb_held = _is_mmb_held(ev)
            except Exception:
                mmb_held = False
            allow_mmb = _allow_mmb_ui(game)
            if is_blocked(mx, my) and not (mmb_held and allow_mmb):
                blocked_idx.add(i)
    remaining_events = [e for idx, e in enumerate(events) if idx not in consumed_idx and idx not in blocked_idx]
    return remaining_events
