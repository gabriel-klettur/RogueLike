from __future__ import annotations

import pygame


class SpawnerTutorialPanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle(self, event) -> bool:
        if not getattr(self.model, 'active', False):
            return False
        # Close with ESC
        if getattr(event, 'type', None) == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
            self.controller.deactivate(force=True)
            return True
        # LMB inside panel: handle buttons and always consume
        if getattr(event, 'type', None) == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = getattr(event, 'pos', None)
            panel_rect = getattr(self.model, 'panel_rect', None)
            if pos and panel_rect and panel_rect.collidepoint(pos):
                rects = getattr(self.model, 'button_rects', {}) or {}
                total = len(getattr(self.model, 'steps', []) or [])
                is_last = (total > 0 and getattr(self.model, 'step_index', 0) >= total - 1)
                # Prev
                r = rects.get('prev')
                if r and r.collidepoint(pos):
                    if getattr(self.model, 'step_index', 0) > 0:
                        new_idx = max(0, int(self.model.step_index) - 1)
                        try:
                            self.controller.on_step_changed(new_idx)
                        except Exception:
                            pass
                        self.model.step_index = new_idx
                    return True
                # Next
                r = rects.get('next')
                if r and r.collidepoint(pos):
                    if not is_last:
                        max_idx = max(0, len(self.model.steps) - 1)
                        new_idx = min(max_idx, int(self.model.step_index) + 1)
                        try:
                            self.controller.on_step_changed(new_idx)
                        except Exception:
                            pass
                        self.model.step_index = new_idx
                    return True
                # Close
                r = rects.get('close')
                if r and r.collidepoint(pos):
                    self.controller.deactivate(force=True)
                    return True
                return True
        return False
