"""
Eventos del panel de Tutorial (Buildings Editor).
"""
import pygame


class BuildingsTutorialPanelEventHandler:
    def __init__(self, state, editor_state, controller, model):
        self.state = state
        self.editor = editor_state
        self.controller = controller
        self.model = model

    def handle(self, event) -> bool:
        if not getattr(self.model, 'active', False):
            return False
        # Cerrar con ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            self.controller.deactivate()
            return True
        # Clicks en botones
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            # Si clic dentro del panel, consumir SIEMPRE para bloquear propagación
            panel_rect = getattr(self.model, 'panel_rect', None)
            if panel_rect and panel_rect.collidepoint(pos):
                # Procesar botones si corresponde
                rects = getattr(self.model, 'button_rects', {}) or {}
                if rects:
                    total = len(getattr(self.model, 'steps', []) or [])
                    is_last = (total > 0 and self.model.step_index >= total - 1)
                    # Prev
                    r = rects.get('prev')
                    if r and r.collidepoint(pos):
                        if self.model.step_index <= 0:
                            return True
                        self.model.step_index = max(0, self.model.step_index - 1)
                        return True
                    # Next
                    r = rects.get('next')
                    if r and r.collidepoint(pos):
                        if is_last:
                            # Deshabilitado en el último paso: consumir sin avanzar
                            return True
                        max_idx = max(0, len(self.model.steps) - 1)
                        self.model.step_index = min(max_idx, self.model.step_index + 1)
                        return True
                    # Close
                    r = rects.get('close')
                    if r and r.collidepoint(pos):
                        self.controller.deactivate()
                        return True
                # Clic dentro del panel pero fuera de botones
                return True
            # Si clic fuera del panel, no consumir para permitir otras UI (toolbar) si el usuario desea
            # El bloqueo visual ya lo gestiona ui_blocker en las vistas.
        return False
