def overlay_consume(game, events):
    overlay = getattr(game.renderer, 'diagnostics_overlay', None)
    consumed_idx = set()
    if overlay and getattr(overlay, 'panel_rect', None):
        pg = __import__('pygame')
        # Detect dragging state from overlay model (safe getattr chain)
        dragging = False
        try:
            dragging = bool(getattr(getattr(overlay, 'model', None), 'dragging', False))
        except Exception:
            dragging = False
        for i, ev in enumerate(events):
            et = getattr(ev, 'type', None)
            is_wheel = et == getattr(pg, 'MOUSEWHEEL', 526)
            is_down = et == getattr(pg, 'MOUSEBUTTONDOWN', 1025)
            is_motion = et == getattr(pg, 'MOUSEMOTION', 1024)
            is_up = et == getattr(pg, 'MOUSEBUTTONUP', 1026)
            # Always forward motion and RMB-up when dragging
            if dragging and (is_motion or (is_up and getattr(ev, 'button', None) == 3)):
                if overlay.handle_event(ev):
                    consumed_idx.add(i)
                continue
            # Otherwise, forward wheel and button-down if the cursor is over the overlay
            if is_wheel or is_down:
                pos = pg.mouse.get_pos() if is_wheel else getattr(ev, 'pos', None)
                if pos is not None and overlay.hit_test(pos):
                    if overlay.handle_event(ev):
                        consumed_idx.add(i)
    return overlay, consumed_idx
