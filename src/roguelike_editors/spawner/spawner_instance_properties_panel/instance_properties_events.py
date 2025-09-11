from __future__ import annotations

import logging


class InstancePropertiesEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        model = controller.model
        view = controller.view
        if not getattr(model, 'visible', False):
            return False
        rect = getattr(view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()

        # Helpers
        y_off = 30
        row_h = 20
        rows = controller.get_rows()
        scroll = int(getattr(model, 'scroll_offset', 0) or 0)
        viewport_h = int(rect.height - 38)

        def compute_row_index(py):
            if not rect.collidepoint((rect.left + 1, py)):
                return None
            local_y = py - rect.top
            if local_y < y_off or local_y > (y_off + viewport_h):
                return None
            i = (local_y - y_off + scroll) // row_h
            gi = int(i)
            if 0 <= gi < len(rows):
                return gi
            return None

        # Visuals table geometry (matches view): compute header and rows areas
        base_rows_h = len(rows) * row_h
        visuals_spacing = 8
        y_start_visuals = y_off + base_rows_h - scroll + visuals_spacing
        y_header = y_start_visuals + row_h
        y_rows = y_header + row_h
        # Locals for hit-testing inside visuals per-row rects
        def hit_visuals_template_index(local_pos):
            tr_list = getattr(view, 'visuals_template_rects', []) or []
            if not tr_list:
                return None
            for j, r in enumerate(tr_list):
                if r and r.collidepoint(local_pos):
                    return j
            return None

        def hit_visuals_plus_index(local_pos):
            pr_list = getattr(view, 'visuals_plus_rects', []) or []
            if not pr_list:
                return None
            for j, r in enumerate(pr_list):
                if r and r.collidepoint(local_pos):
                    return j
            return None

        def hit_visuals_browse_index(local_pos):
            br_list = getattr(view, 'visuals_browse_rects', []) or []
            if not br_list:
                return None
            for j, r in enumerate(br_list):
                if r and r.collidepoint(local_pos):
                    return j
            return None

        def hit_visuals_eye_index(local_pos):
            er_list = getattr(view, 'visuals_eye_rects', []) or []
            if not er_list:
                return None
            for j, r in enumerate(er_list):
                if r and r.collidepoint(local_pos):
                    return j
            return None

        if et == pygame.MOUSEWHEEL and rect.collidepoint(pos):
            # If combo is open and mouse is over its list, scroll combo list
            local = (pos[0] - rect.left, pos[1] - rect.top)
            if getattr(model, 'template_combo_open', False) and getattr(view, 'template_list_rect', None):
                lrect = view.template_list_rect
                if lrect is not None and lrect.collidepoint(local):
                    dy = int(getattr(event, 'y', 0) or 0)
                    opts = controller.get_template_options()
                    visible_rows = min(8, max(1, len(opts)))
                    max_off = max(0, len(opts) - visible_rows)
                    cur_off = int(getattr(model, 'template_scroll_offset', 0) or 0)
                    model.template_scroll_offset = max(0, min(max_off, cur_off - dy))
                    return True
            # Otherwise, scroll the panel
            dy = int(getattr(event, 'y', 0) or 0)
            current = int(getattr(model, 'scroll_offset', 0) or 0)
            new_offset = current - dy * 20
            content_h = int(getattr(view, 'content_height', 0) or 0)
            viewport_h = int(rect.height - 38)
            max_scroll = max(0, content_h - max(0, viewport_h))
            model.scroll_offset = max(0, min(max_scroll, new_offset))
            return True

        # Hover tracking
        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                gi = compute_row_index(pos[1])
                model.hovered_index = gi
                # Update combo hover when open
                if getattr(model, 'template_combo_open', False):
                    local = (pos[0] - rect.left, pos[1] - rect.top)
                    lrect = getattr(view, 'template_list_rect', None)
                    if lrect is not None and lrect.collidepoint(local):
                        row_h_local = row_h
                        start = int(getattr(model, 'template_scroll_offset', 0) or 0)
                        rel_y = local[1] - lrect.y
                        j = int(rel_y // row_h_local)
                        abs_idx = start + j
                        opts = controller.get_template_options()
                        if 0 <= abs_idx < len(opts):
                            model.template_hovered_index = abs_idx
                        else:
                            model.template_hovered_index = None
                    else:
                        model.template_hovered_index = None
                # No explicit hover state for visuals rows for now
                return True
            else:
                model.hovered_index = None

        # If editing normal row, route to text input first
        if controller.is_editing():
            # ESC cancels
            if et == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
                ti = controller.get_text_input()
                if ti is not None:
                    ti.deactivate()
                model.editing_key = None
                model.editing_row_index = None
                return True
            ti = controller.get_text_input()
            handled = False
            if ti is not None:
                if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEMOTION):
                    local = (pos[0] - rect.left, pos[1] - rect.top)
                    payload = {k: getattr(event, k) for k in ('button','rel','x','y') if hasattr(event, k)}
                    payload['pos'] = local
                    fake = pygame.event.Event(et, payload)
                    handled = ti.handle_event(fake)
                else:
                    handled = ti.handle_event(event)
            if handled:
                controller.commit_edit_if_finished()
                return True
            if et == pygame.MOUSEBUTTONDOWN:
                ti = controller.get_text_input()
                if ti is not None:
                    ti_rect_screen = None
                    try:
                        ti_rect_screen = pygame.Rect(rect.left + ti.last_rect.x,
                                                     rect.top + ti.last_rect.y,
                                                     ti.last_rect.width,
                                                     ti.last_rect.height)
                    except Exception:
                        ti_rect_screen = None
                    if ti_rect_screen is None or not ti_rect_screen.collidepoint(pos):
                        ti.deactivate()
                        controller.commit_edit_if_finished()
                        return True

        # If editing a Visuals Template cell, route to its text input
        if getattr(model, 'visuals_editing_state', None) is not None:
            state = model.visuals_editing_state
            ti = controller.get_text_input()
            # Handle ESC / ENTER
            if et == pygame.KEYDOWN:
                key = getattr(event, 'key', None)
                if key == pygame.K_ESCAPE:
                    if ti is not None:
                        ti.deactivate()
                    controller.cancel_edit_visual()
                    try:
                        logging.getLogger(__name__).debug(f"[InstancePropsEvents] Cancel edit visuals for state={state}")
                    except Exception:
                        pass
                    return True
                if key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    if ti is not None:
                        ti.deactivate()
                    controller.commit_visual_edit_if_finished()
                    try:
                        logging.getLogger(__name__).info(f"[InstancePropsEvents] Commit edit visuals for state={state}")
                    except Exception:
                        pass
                    return True
                # Other keys should be forwarded to the text input for live editing
                if ti is not None:
                    handled = ti.handle_event(event)
                    if handled:
                        return True
            # Also forward KEYUP/TEXTINPUT events (if TextInput uses them)
            if et in (pygame.KEYUP, pygame.TEXTINPUT):
                if ti is not None:
                    handled = ti.handle_event(event)
                    if handled:
                        return True
            if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEMOTION):
                local = (pos[0] - rect.left, pos[1] - rect.top)
                # Plus button click creates instance at camera center
                if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                    j = hit_visuals_plus_index(local)
                    if j is not None:
                        rows_v = controller.get_visuals_rows()
                        if 0 <= j < len(rows_v):
                            st = rows_v[j][0]
                            controller.add_building_instance_for_visual(st)
                            try:
                                logging.getLogger(__name__).info(f"[InstancePropsEvents] Click + to add instance for state={st}")
                            except Exception:
                                pass
                            return True
                    j = hit_visuals_browse_index(local)
                    if j is not None:
                        rows_v = controller.get_visuals_rows()
                        if 0 <= j < len(rows_v):
                            st = rows_v[j][0]
                            controller.open_visuals_picker_for_state(st)
                            try:
                                logging.getLogger(__name__).info(f"[InstancePropsEvents] Open VisualsPicker for state={st} (editing)")
                            except Exception:
                                pass
                            return True
                    j = hit_visuals_eye_index(local)
                    if j is not None:
                        rows_v = controller.get_visuals_rows()
                        if 0 <= j < len(rows_v):
                            st = rows_v[j][0]
                            controller.toggle_visual_building_visibility(st)
                            try:
                                logging.getLogger(__name__).info(f"[InstancePropsEvents] Toggle eye for state={st}")
                            except Exception:
                                pass
                            return True
                # Route pointer events to text input when inside template rect
                handled = False
                if ti is not None:
                    j = hit_visuals_template_index(local)
                    if j is not None:
                        payload = {k: getattr(event, k) for k in ('button','rel','x','y') if hasattr(event, k)}
                        # Translate to local coords relative to text input origin
                        payload['pos'] = local
                        fake = pygame.event.Event(et, payload)
                        handled = ti.handle_event(fake)
                if handled:
                    return True
                # Click outside: commit then exit
                if et == pygame.MOUSEBUTTONDOWN:
                    if ti is not None:
                        ti.deactivate()
                    controller.commit_visual_edit_if_finished()
                    return True

        # Consume mouse clicks inside the panel
        if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and rect.collidepoint(pos):
            local = (pos[0] - rect.left, pos[1] - rect.top)
            # Visuals interactions: '+' button and starting edit on Template cell
            if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                j = hit_visuals_plus_index(local)
                if j is not None:
                    rows_v = controller.get_visuals_rows()
                    if 0 <= j < len(rows_v):
                        st = rows_v[j][0]
                        controller.add_building_instance_for_visual(st)
                        try:
                            logging.getLogger(__name__).info(f"[InstancePropsEvents] Click + to add instance for state={st}")
                        except Exception:
                            pass
                        return True
                j = hit_visuals_browse_index(local)
                if j is not None:
                    rows_v = controller.get_visuals_rows()
                    if 0 <= j < len(rows_v):
                        st = rows_v[j][0]
                        controller.open_visuals_picker_for_state(st)
                        try:
                            logging.getLogger(__name__).info(f"[InstancePropsEvents] Open VisualsPicker for state={st}")
                        except Exception:
                            pass
                        return True
                j = hit_visuals_eye_index(local)
                if j is not None:
                    rows_v = controller.get_visuals_rows()
                    if 0 <= j < len(rows_v):
                        st = rows_v[j][0]
                        controller.toggle_visual_building_visibility(st)
                        try:
                            logging.getLogger(__name__).info(f"[InstancePropsEvents] Toggle eye for state={st}")
                        except Exception:
                            pass
                        return True
                j = hit_visuals_template_index(local)
                if j is not None:
                    rows_v = controller.get_visuals_rows()
                    if 0 <= j < len(rows_v):
                        st = rows_v[j][0]
                        controller.begin_edit_visual(st)
                        return True
            if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                # If combo is open, prioritize clicks on its UI
                if getattr(model, 'template_combo_open', False):
                    # Click on list selects
                    lrect = getattr(view, 'template_list_rect', None)
                    if lrect is not None and lrect.collidepoint(local):
                        row_h_local = row_h
                        start = int(getattr(model, 'template_scroll_offset', 0) or 0)
                        rel_y = local[1] - lrect.y
                        j = int(rel_y // row_h_local)
                        abs_idx = start + j
                        controller.select_template_by_index(abs_idx)
                        model.template_combo_open = False
                        model.template_hovered_index = None
                        return True
                # Click outside list closes combo unless on combo rect
                crect = getattr(view, 'template_combo_rect', None)
                if crect is None or not crect.collidepoint(local):
                    model.template_combo_open = False
                    model.template_hovered_index = None
                    # Do not return; keep processing
                # Toggle combo if clicking on template_id row's combo rect
                crect = getattr(view, 'template_combo_rect', None)
                if crect is not None and crect.collidepoint(local):
                    model.template_combo_open = not bool(getattr(model, 'template_combo_open', False))
                    # When opening, initialize hovered/scroll to current selection
                    if model.template_combo_open:
                        cur_idx = controller.get_current_template_index()
                        opts = controller.get_template_options()
                        visible_rows = min(8, max(1, len(opts)))
                        if cur_idx is None:
                            model.template_hovered_index = 0 if len(opts) > 0 else None
                            model.template_scroll_offset = 0
                        else:
                            model.template_hovered_index = cur_idx
                            model.template_scroll_offset = max(0, min(max(0, len(opts) - visible_rows), cur_idx))
                    return True
                # On click on a row: open text edit unless it's template_id row
                gi = compute_row_index(pos[1])
                if gi is not None:
                    try:
                        rows = controller.get_rows()
                        key, _ = rows[gi]
                    except Exception:
                        key = None
                    if str(key) == 'template_id':
                        # Click on template row but not on combo box area -> toggle too
                        model.template_combo_open = not bool(getattr(model, 'template_combo_open', False))
                        if model.template_combo_open:
                            cur_idx = controller.get_current_template_index()
                            opts = controller.get_template_options()
                            visible_rows = min(8, max(1, len(opts)))
                            if cur_idx is None:
                                model.template_hovered_index = 0 if len(opts) > 0 else None
                                model.template_scroll_offset = 0
                            else:
                                model.template_hovered_index = cur_idx
                                model.template_scroll_offset = max(0, min(max(0, len(opts) - visible_rows), cur_idx))
                        return True
                    # Default behavior: double click to edit
                    if controller._dbl.is_double_click(('inst_prop', gi)):
                        controller.begin_edit_row(gi)
                        return True
            return True

        # Keyboard for combo when open
        if et == pygame.KEYDOWN and getattr(model, 'template_combo_open', False):
            key = getattr(event, 'key', None)
            opts = controller.get_template_options()
            if key == pygame.K_ESCAPE:
                model.template_combo_open = False
                model.template_hovered_index = None
                return True
            if key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                idx = getattr(model, 'template_hovered_index', None)
                if idx is None:
                    idx = controller.get_current_template_index() or 0
                if 0 <= idx < len(opts):
                    controller.select_template_by_index(idx)
                model.template_combo_open = False
                model.template_hovered_index = None
                return True
            if key in (pygame.K_UP, pygame.K_DOWN):
                cur = getattr(model, 'template_hovered_index', None)
                if cur is None:
                    cur = controller.get_current_template_index()
                if cur is None:
                    cur = 0
                delta = -1 if key == pygame.K_UP else 1
                new_idx = max(0, min(len(opts) - 1, cur + delta))
                model.template_hovered_index = new_idx
                # keep in view
                visible_rows = min(8, max(1, len(opts)))
                start = int(getattr(model, 'template_scroll_offset', 0) or 0)
                end = start + visible_rows - 1
                if new_idx < start:
                    model.template_scroll_offset = new_idx
                elif new_idx > end:
                    model.template_scroll_offset = new_idx - visible_rows + 1
                return True

        return False
