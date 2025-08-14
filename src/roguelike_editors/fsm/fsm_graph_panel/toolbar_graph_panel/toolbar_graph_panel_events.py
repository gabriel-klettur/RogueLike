from __future__ import annotations

import logging
LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.controller")


class FsmGraphToolbarEventHandler:
    def handle_event(self, event, *, canvas_rect, graph_model) -> bool:
        """
        Keyboard shortcuts for graph toolbar when the mouse is over the graph canvas:
        - '+' => zoom in
        - '-' => zoom out
        - Mouse wheel up/down => zoom in/out
        Returns True if handled.
        """
        try:
            import pygame  # type: ignore
        except Exception:
            return False

        et = getattr(event, 'type', None)

        # Only when mouse is over the graph canvas
        mouse_pos = pygame.mouse.get_pos()
        if not canvas_rect or not canvas_rect.collidepoint(mouse_pos):
            return False

        # Mouse wheel zoom
        if et == pygame.MOUSEWHEEL:
            try:
                y = int(getattr(event, 'y', 0))
            except Exception:
                y = 0
            if y == 0:
                return False
            factor = 1.1 ** y
            old_z = max(0.05, float(getattr(graph_model, 'zoom', 1.0)))
            new_z = max(0.2, min(3.0, old_z * factor))
            if abs(new_z - old_z) <= 1e-6:
                return True
            LOGGER.debug("[GraphToolbar][WHEEL] y=%s pos=%s old_z=%.3f -> new_z=%.3f", y, mouse_pos, old_z, new_z)
            # zoom around mouse
            local_x = mouse_pos[0] - canvas_rect.left
            local_y = mouse_pos[1] - canvas_rect.top
            pan_x = float(getattr(graph_model, 'pan_x', 0.0))
            pan_y = float(getattr(graph_model, 'pan_y', 0.0))
            wx = (local_x - pan_x) / old_z
            wy = (local_y - pan_y) / old_z
            graph_model.zoom = new_z
            graph_model.pan_x = local_x - wx * new_z
            graph_model.pan_y = local_y - wy * new_z
            LOGGER.debug("[GraphToolbar][WHEEL] updated pan=(%.1f,%.1f)", graph_model.pan_x, graph_model.pan_y)
            return True

        if et != pygame.KEYDOWN:
            return False

        key = getattr(event, 'key', None)
        mod = getattr(event, 'mod', 0)
        uni = getattr(event, 'unicode', '') or ''

        # Robust plus/minus detection across layouts and keypad
        K_PLUS = getattr(pygame, 'K_PLUS', None)
        K_KP_PLUS = getattr(pygame, 'K_KP_PLUS', None)
        K_MINUS = getattr(pygame, 'K_MINUS', None)
        K_KP_MINUS = getattr(pygame, 'K_KP_MINUS', None)
        K_EQUALS = getattr(pygame, 'K_EQUALS', None)

        is_plus = False
        if key is not None:
            if (K_PLUS is not None and key == K_PLUS) or (K_KP_PLUS is not None and key == K_KP_PLUS):
                is_plus = True
            # Shift + '=' commonly produces '+' on US keyboards
            if not is_plus and (K_EQUALS is not None and key == K_EQUALS and (mod & pygame.KMOD_SHIFT)):
                is_plus = True
        if not is_plus and uni == '+':
            is_plus = True

        is_minus = False
        if key is not None:
            if (K_MINUS is not None and key == K_MINUS) or (K_KP_MINUS is not None and key == K_KP_MINUS):
                is_minus = True
        if not is_minus and uni == '-':
            is_minus = True

        if not (is_plus or is_minus):
            return False

        # Apply zoom around canvas center, mimicking toolbar button behavior
        old_z = max(0.05, float(getattr(graph_model, 'zoom', 1.0)))
        factor = 1.1 if is_plus else (1/1.1)
        new_z = max(0.2, min(3.0, old_z * factor))
        if abs(new_z - old_z) <= 1e-6:
            return True
        LOGGER.debug("[GraphToolbar][KEY %s] factor=%.3f old_z=%.3f -> new_z=%.3f", '+' if is_plus else '-', factor, old_z, new_z)

        # Canvas center in screen coordinates
        cx = canvas_rect.left + canvas_rect.w // 2
        cy = canvas_rect.top + canvas_rect.h // 2
        lcx = cx - canvas_rect.left
        lcy = cy - canvas_rect.top
        pan_x = float(getattr(graph_model, 'pan_x', 0.0))
        pan_y = float(getattr(graph_model, 'pan_y', 0.0))
        wx = (lcx - pan_x) / old_z
        wy = (lcy - pan_y) / old_z
        graph_model.zoom = new_z
        graph_model.pan_x = lcx - wx * new_z
        graph_model.pan_y = lcy - wy * new_z
        LOGGER.debug("[GraphToolbar][KEY %s] updated pan=(%.1f,%.1f)", '+' if is_plus else '-', graph_model.pan_x, graph_model.pan_y)
        return True


__all__ = ["FsmGraphToolbarEventHandler"]
