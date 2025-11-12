from __future__ import annotations

import pygame


def handle_event(ctl, event: pygame.event.Event) -> bool:
    """Handle ESC, mouse wheel, and scrollbar interactions. Return True if consumed."""
    st = getattr(ctl, "model", None)
    if st is None:
        return False

    # Cancel spawn/drag/selection with ESC
    if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
        try:
            st.spawn_mode = False
            if getattr(st, "_dragging_inst", False):
                st._dragging_inst = False
            st.selected_light_id = None
            try:
                st.selected_light_ids.clear()
            except Exception:
                pass
        except Exception:
            pass
        return True

    # Mouse wheel scroll in panel
    if event.type == pygame.MOUSEWHEEL:
        try:
            so = int(getattr(st, "scroll_offset", 0))
            vp = getattr(st, "_viewport_rect", None)
            ch = int(getattr(st, "_content_height", 0))
            if isinstance(vp, pygame.Rect) and ch > vp.height:
                step = st.row_h
                so = max(0, min(ch - vp.height, so - event.y * step))
                st.scroll_offset = so
        except Exception:
            pass
        return True

    # Scrollbar drag start and track clicks
    if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, "button", None) == 1:
        thumb = getattr(st, "_scrollbar_thumb", None)
        track = getattr(st, "_scrollbar_track", None)
        if isinstance(thumb, pygame.Rect) and thumb.collidepoint(event.pos):
            st._dragging_scroll = True
            st._drag_start_y = event.pos[1]
            st._drag_start_offset = int(getattr(st, "scroll_offset", 0))
            return True
        if isinstance(track, pygame.Rect) and track.collidepoint(event.pos) and (not isinstance(thumb, pygame.Rect) or not thumb.collidepoint(event.pos)):
            try:
                vp = getattr(st, "_viewport_rect", None)
                ch = int(getattr(st, "_content_height", 0))
                if isinstance(vp, pygame.Rect) and ch > vp.height:
                    page = max(st.row_h, vp.height - st.row_h)
                    so = int(getattr(st, "scroll_offset", 0))
                    if event.pos[1] < thumb.top:
                        so = max(0, so - page)
                    else:
                        so = min(ch - vp.height, so + page)
                    st.scroll_offset = so
            except Exception:
                pass
            return True

    if event.type == pygame.MOUSEMOTION and bool(getattr(st, "_dragging_scroll", False)):
        try:
            track = getattr(st, "_scrollbar_track", None)
            thumb = getattr(st, "_scrollbar_thumb", None)
            vp = getattr(st, "_viewport_rect", None)
            ch = int(getattr(st, "_content_height", 0))
            if isinstance(track, pygame.Rect) and isinstance(thumb, pygame.Rect) and isinstance(vp, pygame.Rect) and ch > vp.height:
                dy = int(event.pos[1] - int(getattr(st, "_drag_start_y", 0) or 0))
                denom = max(1, track.height - thumb.height)
                frac = max(0.0, min(1.0, float(dy) / float(denom)))
                max_off = ch - vp.height
                base_off = int(getattr(st, "_drag_start_offset", 0) or 0)
                st.scroll_offset = max(0, min(max_off, base_off + int(frac * max_off)))
        except Exception:
            pass
        return True

    if event.type == pygame.MOUSEBUTTONUP and getattr(st, "_dragging_scroll", False):
        st._dragging_scroll = False
        st._drag_start_y = None
        st._drag_start_offset = None
        return True

    return False
