import pygame


def early_rmb_drag_to_move_panel(editor, ev) -> None:
    if (
        ev.type == pygame.MOUSEBUTTONDOWN
        and getattr(ev, "button", None) == 3
        and getattr(editor, "picker_active", False)
    ):
        try:
            panel_rect = getattr(editor, "picker_panel_rect", None)
            if panel_rect:
                mx, my = getattr(ev, "pos", (None, None))
                if mx is not None and panel_rect.collidepoint(mx, my):
                    m = int(getattr(editor, "picker_internal_margin", 8) or 8)
                    pad = int(getattr(editor, "picker_padding", 8) or 8)
                    cw = int(getattr(editor, "picker_cell_w", 64) or 64)
                    ch = int(getattr(editor, "picker_cell_h", 64) or 64)
                    footer_h = int(getattr(editor, "picker_footer_h", 0) or 0)
                    needs_scroll = bool(getattr(editor, "picker_needs_scroll", False))
                    sb_pad = 4
                    sb_w = int(getattr(editor, "picker_scrollbar_w", 10) or 10) if needs_scroll else 0
                    gx = panel_rect.left + m
                    gy = panel_rect.top + m
                    gw = max(0, panel_rect.w - 2 * m)
                    gh = max(0, panel_rect.h - 2 * m - footer_h)
                    gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
                    track_rect = getattr(editor, "picker_scroll_track_rect", None)
                    in_grid = pygame.Rect(gx, gy, gw, gh).collidepoint(mx, my)
                    in_scroll = needs_scroll and (
                        (track_rect and pygame.Rect(track_rect).collidepoint(mx, my)) or (mx >= gx + gw_effective)
                    )
                    if (not in_grid) and (not in_scroll):
                        editor.picker_dragging_panel = True
                        editor.picker_drag_offset = (mx - panel_rect.left, my - panel_rect.top)
                        if getattr(editor, "picker_manual_pos", None) is None:
                            editor.picker_manual_pos = (panel_rect.left, panel_rect.top)
        except Exception:
            pass


def handle_picker_event(editor, picker_events, ev, camera) -> bool:
    if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL) and getattr(editor, "picker_active", False):
        try:
            panel_rect = getattr(editor, "picker_panel_rect", None)
            if panel_rect:
                if ev.type == pygame.MOUSEWHEEL:
                    mx, my = pygame.mouse.get_pos()
                else:
                    mx, my = getattr(ev, "pos", (None, None))
                if mx is not None and panel_rect.collidepoint(mx, my):
                    if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, "button", None) == 3:
                        try:
                            m = int(getattr(editor, "picker_internal_margin", 8) or 8)
                            pad = int(getattr(editor, "picker_padding", 8) or 8)
                            cw = int(getattr(editor, "picker_cell_w", 64) or 64)
                            ch = int(getattr(editor, "picker_cell_h", 64) or 64)
                            footer_h = int(getattr(editor, "picker_footer_h", 0) or 0)
                            needs_scroll = bool(getattr(editor, "picker_needs_scroll", False))
                            sb_pad = 4
                            sb_w = int(getattr(editor, "picker_scrollbar_w", 10) or 10) if needs_scroll else 0
                            gx = panel_rect.left + m
                            gy = panel_rect.top + m
                            gw = max(0, panel_rect.w - 2 * m)
                            gh = max(0, panel_rect.h - 2 * m - footer_h)
                            gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))
                            track_rect = getattr(editor, "picker_scroll_track_rect", None)
                            in_grid = pygame.Rect(gx, gy, gw, gh).collidepoint(mx, my)
                            in_scroll = needs_scroll and (
                                (track_rect and pygame.Rect(track_rect).collidepoint(mx, my)) or (mx >= gx + gw_effective)
                            )
                            if (not in_grid) and (not in_scroll):
                                editor.picker_dragging_panel = True
                                editor.picker_drag_offset = (mx - panel_rect.left, my - panel_rect.top)
                                if getattr(editor, "picker_manual_pos", None) is None:
                                    editor.picker_manual_pos = (panel_rect.left, panel_rect.top)
                        except Exception:
                            pass
                    picker_events.handle(ev, camera)
                    return True
        except Exception:
            pass
    return False
