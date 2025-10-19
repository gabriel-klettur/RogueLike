import pygame

def filter_minimap_events(game, events):
    mm = getattr(game, 'minimap', None)
    if mm is None:
        return events
    filtered = []
    for ev in events:
        try:
            if ev.type in (pygame.MOUSEMOTION, pygame.MOUSEBUTTONDOWN):
                if mm.handle_event(ev, game.screen):
                    continue
        except Exception:
            pass
        filtered.append(ev)
    return filtered
