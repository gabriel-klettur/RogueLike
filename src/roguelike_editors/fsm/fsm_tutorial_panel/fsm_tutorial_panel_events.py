"""
Eventos del panel de Tutorial (FSM Editor).
"""
from __future__ import annotations
import pygame


class FsmTutorialPanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle(self, event) -> bool:
        if not getattr(self.model, 'active', False):
            return False
        # Cerrar con ESC
        if event.type == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
            self.controller.deactivate()
            return True
        # Clicks en botones
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = getattr(event, 'pos', None)
            if pos is None:
                return False
            panel_rect = getattr(self.model, 'panel_rect', None)
            if panel_rect and panel_rect.collidepoint(pos):
                rects = getattr(self.model, 'button_rects', {}) or {}
                total = len(getattr(self.model, 'steps', []) or [])
                is_last = (total > 0 and self.model.step_index >= total - 1)
                r = rects.get('prev')
                if r and r.collidepoint(pos):
                    if self.model.step_index <= 0:
                        return True
                    self.controller.on_step_changed(max(0, self.model.step_index - 1))
                    self.model.step_index = max(0, self.model.step_index - 1)
                    return True
                r = rects.get('next')
                if r and r.collidepoint(pos):
                    if is_last:
                        return True
                    max_idx = max(0, len(self.model.steps) - 1)
                    new_idx = min(max_idx, self.model.step_index + 1)
                    self.controller.on_step_changed(new_idx)
                    self.model.step_index = new_idx
                    return True
                r = rects.get('close')
                if r and r.collidepoint(pos):
                    self.controller.deactivate()
                    return True
                return True  # click dentro del panel: bloquear
        return False
