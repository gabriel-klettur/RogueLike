from __future__ import annotations

import logging
logger = logging.getLogger(__name__)

class FsmToolbarEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False

        # Clear active tool with ESC
        if getattr(event, 'type', None) == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
            if getattr(controller.model, 'active_tool', None) is not None:
                controller.set_active(None)
                return True
            return False

        # Toggle 'sets' tool with the 'S' key
        if getattr(event, 'type', None) == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_s:
            new_state = None if controller.is_active('sets') else 'sets'
            controller.set_active(new_state)
            logger.debug("[FSMToolbar][KEY S] toggled 'sets' -> active_tool=%s", new_state)
            return True

        toolbar = getattr(controller.view, 'toolbar', None)
        if toolbar is None:
            return False

        # Helper: panel rect
        panel_pos = toolbar.panel.pos or (toolbar.x, toolbar.y)
        panel_rect = pygame.Rect(panel_pos, toolbar.panel.surface.get_size())

        # Block mouse wheel over toolbar (avoid zoom/scroll elsewhere)
        if getattr(event, 'type', None) == pygame.MOUSEWHEEL:
            mouse_pos = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mouse_pos):
                logger.debug("[FSMToolbar][WHEEL] consumed over toolbar: x=%s y=%s", mouse_pos[0], mouse_pos[1])
                return True

        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            # Left click inside toolbar area: handle strictly; block others
            pos = getattr(event, 'pos', None)
            if not pos:
                return False
            if not panel_rect.collidepoint(pos):
                return False  # Click afuera: no consumimos
            # 1) Intentar click sobre icono (acción válida)
            for tool, rect in getattr(toolbar, 'icon_rects', {}).items():
                if rect.collidepoint(pos):
                    new_state = None if controller.is_active(tool) else tool
                    controller.set_active(new_state)
                    logger.debug("[FSMToolbar][CLICK ICON] tool=%s -> active_tool=%s", tool, new_state)
                    return True
            # 2) Click en el fondo del toolbar: no hay acción; solo bloquear
            logger.debug("[FSMToolbar][CLICK BG] blocked (no action)")
            # Consumir siempre el click dentro del toolbar para bloquear otras acciones
            return True
        # Consumir otros clicks dentro del panel (excepto RMB para arrastre que gestiona DraggablePanel)
        if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            pos = getattr(event, 'pos', None)
            if pos and panel_rect.collidepoint(pos):
                if getattr(event, 'button', None) == 3:
                    # Permitir arrastre del panel; no consumimos aquí
                    return False
                logger.debug("[FSMToolbar][CLICK OTHER] consumed event=%s button=%s", event.type, getattr(event, 'button', None))
                return True
        return False


__all__ = ["FsmToolbarEventHandler"]
