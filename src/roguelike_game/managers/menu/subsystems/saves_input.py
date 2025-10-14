from __future__ import annotations

import time
from typing import Optional, Tuple

import pygame


def handle_event(mgr, event) -> None:
    """Delegate all SaveListManager input handling here.

    This function mirrors the original handle_input logic, operating on the
    passed-in manager instance to mutate state and call existing methods.
    """
    if event.type == pygame.KEYDOWN:
        if mgr._saves_show_confirm_delete:
            if event.key == pygame.K_ESCAPE:
                mgr._saves_show_confirm_delete = False
                mgr._saves_hover_confirm_yes = False
                mgr._saves_hover_confirm_cancel = False
                return None
            if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                mgr._confirm_delete_selected_save()
                return None
            return None
        if mgr._saves_editing_name:
            if event.key == pygame.K_ESCAPE:
                mgr._end_edit_save_name(cancel=True)
                return None
            if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                mgr._commit_save_rename()
                return None
            # Delegate rest of editing keys to inline editor
            mgr.editor.text = mgr._saves_edit_name_text
            mgr.editor.caret = mgr._saves_edit_caret
            mgr.editor.select_all = mgr._saves_select_all_edit
            mgr.editor.handle_key(event)
            mgr._saves_edit_name_text = mgr.editor.text
            mgr._saves_edit_caret = mgr.editor.caret
            mgr._saves_select_all_edit = mgr.editor.select_all
            return None
        if event.key in (pygame.K_UP, pygame.K_w, pygame.K_a):
            if mgr.save_entries:
                mgr.load_selected = (mgr.load_selected - 1) % len(mgr.save_entries)
                mgr._end_edit_save_name(cancel=True)
                layout = getattr(mgr.renderer, "last_saves_layout", None)
                if layout:
                    start = layout.get("start", 0)
                    if mgr.load_selected < start:
                        mgr._saves_row_scroll_offset = mgr.load_selected
        elif event.key in (pygame.K_DOWN, pygame.K_s, pygame.K_d):
            if mgr.save_entries:
                mgr.load_selected = (mgr.load_selected + 1) % len(mgr.save_entries)
                mgr._end_edit_save_name(cancel=True)
                layout = getattr(mgr.renderer, "last_saves_layout", None)
                if layout:
                    start = layout.get("start", 0)
                    end = layout.get("end", 0)
                    visible = max(1, end - start)
                    if mgr.load_selected >= end:
                        mgr._saves_row_scroll_offset = max(0, mgr.load_selected - (visible - 1))
        elif event.key in (pygame.K_PAGEUP,):
            layout = getattr(mgr.renderer, "last_saves_layout", {})
            start = layout.get("start", 0)
            max_jump = max(1, (layout.get("end", 0) - start))
            mgr._saves_row_scroll_offset = max(0, mgr._saves_row_scroll_offset - max_jump)
        elif event.key in (pygame.K_PAGEDOWN,):
            layout = getattr(mgr.renderer, "last_saves_layout", {})
            start = layout.get("start", 0)
            max_jump = max(1, (layout.get("end", 0) - start))
            max_off = max(0, len(mgr.save_entries) - max_jump)
            mgr._saves_row_scroll_offset = min(max_off, mgr._saves_row_scroll_offset + max_jump)
        elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
            return None
        return None

    if event.type == pygame.MOUSEMOTION:
        if mgr._saves_show_confirm_delete:
            mgr._saves_hover_confirm_yes = False
            mgr._saves_hover_confirm_cancel = False
            layout_c = getattr(mgr.renderer, "last_confirm_layout", None)
            if layout_c:
                yes_rect = layout_c.get("yes_rect")
                cancel_rect = layout_c.get("cancel_rect")
                if yes_rect and yes_rect.collidepoint(event.pos):
                    mgr._saves_hover_confirm_yes = True
                if cancel_rect and cancel_rect.collidepoint(event.pos):
                    mgr._saves_hover_confirm_cancel = True
            return None
        layout = getattr(mgr.renderer, "last_saves_layout", None)
        mgr._saves_hovered_idx = None
        mgr._saves_hover_details_name = False
        mgr._saves_hover_load_button = False
        mgr._saves_hover_delete_button = False
        if layout:
            for idx, rect in layout.get("row_rects", {}).items():
                if rect.collidepoint(event.pos):
                    mgr._saves_hovered_idx = idx
                    break
            name_rect = layout.get("details_name_rect")
            if name_rect and name_rect.collidepoint(event.pos):
                mgr._saves_hover_details_name = True
            btn_rect = layout.get("load_button_rect")
            if btn_rect and btn_rect.collidepoint(event.pos):
                mgr._saves_hover_load_button = True
            del_rect = layout.get("delete_button_rect")
            if del_rect and del_rect.collidepoint(event.pos):
                mgr._saves_hover_delete_button = True

    elif event.type == pygame.MOUSEWHEEL:
        mgr._saves_row_scroll_offset = max(0, mgr._saves_row_scroll_offset - event.y)

    elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
        if mgr._saves_show_confirm_delete:
            layout_c = getattr(mgr.renderer, "last_confirm_layout", None)
            if layout_c:
                panel_rect = layout_c.get("panel_rect")
                yes_rect = layout_c.get("yes_rect")
                cancel_rect = layout_c.get("cancel_rect")
                if yes_rect and yes_rect.collidepoint(event.pos):
                    mgr._confirm_delete_selected_save()
                    return None
                if cancel_rect and cancel_rect.collidepoint(event.pos):
                    mgr._saves_show_confirm_delete = False
                    return None
                if panel_rect and not panel_rect.collidepoint(event.pos):
                    mgr._saves_show_confirm_delete = False
                    return None
            return None
        layout = getattr(mgr.renderer, "last_saves_layout", None)
        if layout:
            btn_rect = layout.get("load_button_rect")
            if btn_rect and btn_rect.collidepoint(event.pos):
                mgr._load_selected_save()
                return None
            del_rect = layout.get("delete_button_rect")
            if del_rect and del_rect.collidepoint(event.pos):
                mgr._saves_show_confirm_delete = True
                mgr._end_edit_save_name(cancel=False)
                return None
            name_rect = layout.get("details_name_rect")
            if name_rect and name_rect.collidepoint(event.pos):
                now = time.time()
                dbl = False
                if mgr._last_click_time and mgr._last_click_pos:
                    dt = now - mgr._last_click_time
                    dx = abs(event.pos[0] - mgr._last_click_pos[0])
                    dy = abs(event.pos[1] - mgr._last_click_pos[1])
                    if dt <= 0.35 and dx <= 6 and dy <= 6:
                        dbl = True
                mgr._last_click_time = now
                mgr._last_click_pos = event.pos
                if dbl:
                    mgr._begin_edit_save_name()
                    mgr._saves_select_all_edit = True
                    try:
                        mgr.editor.select_all = True
                    except Exception:
                        pass
                else:
                    if mgr._saves_editing_name:
                        mgr._set_caret_from_click(event.pos)
                        mgr._saves_select_all_edit = False
                return None
            for idx, rect in layout.get("row_rects", {}).items():
                if rect.collidepoint(event.pos):
                    mgr.load_selected = idx
                    mgr._end_edit_save_name(cancel=True)
                    break
        return None

    return None
