from __future__ import annotations

from typing import Optional

try:
    import pygame  # type: ignore
except Exception:  # pragma: no cover
    pygame = None  # type: ignore


class VisualizerEvents:
    """Event handler for the Visuals table (buttons, text input) inside the
    Instance Properties panel. Delegates data changes to the parent controller.
    """

    def _hit_index(self, rects, local_pos) -> Optional[int]:
        if not rects:
            return None
        for j, r in enumerate(rects):
            try:
                if r and r.collidepoint(local_pos):
                    return j
            except Exception:
                continue
        return None

    def handle_event(self, controller, event, panel_rect) -> bool:
        # controller is VisualizerController
        if pygame is None or panel_rect is None:
            return False
        pc = controller.parent
        model = pc.model
        vmodel = controller.model
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        if not panel_rect.collidepoint(pos):
            # Only care about events inside the panel
            # Still allow keyboard commits when editing
            if getattr(model, 'visuals_editing_state', None) is not None and et in (pygame.KEYDOWN, pygame.KEYUP, pygame.TEXTINPUT):
                return self._handle_edit_keyboard(controller, event)
            # Clear hover when moving outside
            if et == pygame.MOUSEMOTION:
                try:
                    controller.model.hover_row_index = None
                except Exception:
                    pass
            return False
        # Translate to panel-local coordinates
        local = (pos[0] - panel_rect.left, pos[1] - panel_rect.top)

        # Update hover index on mouse move
        if et == pygame.MOUSEMOTION:
            try:
                j = self._hit_index(getattr(controller.model, 'visuals_row_rects', []) or [], local)
                controller.model.hover_row_index = j
            except Exception:
                pass

        # If editing a Visuals Template cell, route to its text input first
        if getattr(model, 'visuals_editing_state', None) is not None:
            handled = self._handle_edit_mode(controller, event, local)
            if handled:
                return True
            # If click outside input but inside panel, commit and exit
            if et == pygame.MOUSEBUTTONDOWN:
                vti = getattr(vmodel, 'text_input', None)
                if vti is not None:
                    try:
                        vti.deactivate()
                    except Exception:
                        pass
                pc.commit_visual_edit_if_finished()
                return True
            return False

        # Not editing: handle button hits (plus/browse/eye/clear) and starting edit
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            # '+' button
            j = self._hit_index(getattr(vmodel, 'visuals_plus_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.add_instance_for_state(st)
                    return True
            # Browse (folder)
            j = self._hit_index(getattr(vmodel, 'visuals_browse_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.open_picker(st)
                    return True
            # Eye (toggle)
            j = self._hit_index(getattr(vmodel, 'visuals_eye_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.toggle_building_visibility_for_state(st)
                    return True
            # Clear (X)
            j = self._hit_index(getattr(vmodel, 'visuals_clear_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.clear_visual_for_state(st)
                    return True
            # Click on template cell begins text edit
            j = self._hit_index(getattr(vmodel, 'visuals_template_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.begin_edit_visual(st)
                    return True
            # Row hold-to-center: clicking on empty row space starts hold
            j = self._hit_index(getattr(vmodel, 'visuals_row_rects', []) or [], local)
            if j is not None:
                # Avoid starting hold if the click was on any control
                hit_any_control = False
                try:
                    if (self._hit_index(getattr(vmodel, 'visuals_plus_rects', []) or [], local) is not None or
                        self._hit_index(getattr(vmodel, 'visuals_browse_rects', []) or [], local) is not None or
                        self._hit_index(getattr(vmodel, 'visuals_eye_rects', []) or [], local) is not None or
                        self._hit_index(getattr(vmodel, 'visuals_clear_rects', []) or [], local) is not None or
                        self._hit_index(getattr(vmodel, 'visuals_template_rects', []) or [], local) is not None):
                        hit_any_control = True
                except Exception:
                    hit_any_control = False
                if not hit_any_control:
                    rows_v = pc.get_visuals_rows()
                    if 0 <= j < len(rows_v):
                        st = rows_v[j][0]
                        try:
                            controller.model.hold_active = True
                            controller.model.hold_row_index = j
                            # Immediately center once
                            controller.center_camera_on_state(st)
                            # Suppress gameplay camera follow if available
                            try:
                                world = getattr(getattr(pc, 'game', None), 'ecs', None)
                                world = getattr(world, 'ecs_world', None)
                                if world is not None and hasattr(world, 'state'):
                                    setattr(world.state, 'spawner_hold_focus', True)
                            except Exception:
                                pass
                        except Exception:
                            pass
                        return True

        return False

    def _handle_edit_keyboard(self, controller, event) -> bool:
        pc = controller.parent
        if pygame is None:
            return False
        et = getattr(event, 'type', None)
        if et == pygame.KEYDOWN:
            key = getattr(event, 'key', None)
            if key == pygame.K_ESCAPE:
                vti = getattr(controller.model, 'text_input', None)
                if vti is not None:
                    try:
                        vti.deactivate()
                    except Exception:
                        pass
                pc.cancel_edit_visual()
                return True
            if key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                vti = getattr(controller.model, 'text_input', None)
                if vti is not None:
                    try:
                        vti.deactivate()
                    except Exception:
                        pass
                pc.commit_visual_edit_if_finished()
                return True
        # Forward other key events to text input
        vti = getattr(controller.model, 'text_input', None)
        if vti is not None:
            handled = vti.handle_event(event)
            if handled:
                return True
        return False

    def _handle_edit_mode(self, controller, event, local) -> bool:
        pc = controller.parent
        vmodel = controller.model
        if pygame is None:
            return False
        et = getattr(event, 'type', None)
        # Allow mouse interactions on buttons while editing
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            j = self._hit_index(getattr(vmodel, 'visuals_plus_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.add_instance_for_state(st)
                    return True
            j = self._hit_index(getattr(vmodel, 'visuals_browse_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.open_picker(st)
                    return True
            j = self._hit_index(getattr(vmodel, 'visuals_eye_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.toggle_building_visibility_for_state(st)
                    return True
            # Clear (X) while editing
            j = self._hit_index(getattr(vmodel, 'visuals_clear_rects', []) or [], local)
            if j is not None:
                rows_v = pc.get_visuals_rows()
                if 0 <= j < len(rows_v):
                    st = rows_v[j][0]
                    controller.clear_visual_for_state(st)
                    return True
        # Route pointer events inside template rect to TextInput
        vti = getattr(controller.model, 'text_input', None)
        if vti is not None and et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEMOTION):
            j = self._hit_index(getattr(vmodel, 'visuals_template_rects', []) or [], local)
            if j is not None:
                payload = {k: getattr(event, k) for k in ('button', 'rel', 'x', 'y') if hasattr(event, k)}
                payload['pos'] = local
                fake = pygame.event.Event(et, payload)
                if vti.handle_event(fake):
                    return True
        # Route keyboard events when editing
        if et in (pygame.KEYDOWN, pygame.KEYUP, pygame.TEXTINPUT):
            if self._handle_edit_keyboard(controller, event):
                return True
        return False


__all__ = ["VisualizerEvents"]
