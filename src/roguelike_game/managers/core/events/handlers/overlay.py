def overlay_consume(game, events):
    overlay = getattr(game.renderer, 'diagnostics_overlay', None)
    consumed_idx = set()
    if overlay and getattr(overlay, 'panel_rect', None):
        for i, ev in enumerate(events):
            if ev.type in (getattr(__import__('pygame'), 'MOUSEWHEEL', 526), getattr(__import__('pygame'), 'MOUSEBUTTONDOWN', 1025)):
                pos = getattr(__import__('pygame').mouse, 'get_pos')() if ev.type == __import__('pygame').MOUSEWHEEL else getattr(ev, 'pos', None)
                if pos is not None and overlay.hit_test(pos):
                    if overlay.handle_event(ev):
                        consumed_idx.add(i)
    return overlay, consumed_idx
