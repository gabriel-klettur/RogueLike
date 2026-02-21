"""
Eventos del panel de Tutorial (Spells Editor).
"""
import pygame


class SpellsTutorialPanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle(self, event: pygame.event.Event) -> bool:
        if not getattr(self.model, 'active', False):
            return False
        # Cerrar con ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            self.controller.deactivate()
            return True
        # Clicks en botones dentro del panel
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            panel_rect = getattr(self.model, 'panel_rect', None)
            if panel_rect and panel_rect.collidepoint(pos):
                rects = getattr(self.model, 'button_rects', {}) or {}
                if rects:
                    total = len(getattr(self.model, 'steps', []) or [])
                    is_last = (total > 0 and self.model.step_index >= total - 1)
                    # Prev
                    r = rects.get('prev')
                    if r and r.collidepoint(pos):
                        if self.model.step_index <= 0:
                            return True
                        new_idx = max(0, self.model.step_index - 1)
                        try:
                            self.controller.on_step_changed(new_idx)
                        except Exception:
                            pass
                        self.model.step_index = new_idx
                        return True
                    # Next
                    r = rects.get('next')
                    if r and r.collidepoint(pos):
                        if is_last:
                            return True
                        max_idx = max(0, len(self.model.steps) - 1)
                        new_idx = min(max_idx, self.model.step_index + 1)
                        try:
                            self.controller.on_step_changed(new_idx)
                        except Exception:
                            pass
                        self.model.step_index = new_idx
                        return True
                    # Close
                    r = rects.get('close')
                    if r and r.collidepoint(pos):
                        self.controller.deactivate()
                        return True
                return True
        return False
