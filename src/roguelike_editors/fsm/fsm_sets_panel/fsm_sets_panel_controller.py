from __future__ import annotations
from typing import Optional

from .fsm_sets_panel_model import FsmSetsPanelModel
from .fsm_sets_panel_view import FsmSetsPanelView


class FsmSetsPanelController:
    def __init__(self, model: Optional[FsmSetsPanelModel] = None, view: Optional[FsmSetsPanelView] = None) -> None:
        self.model = model or FsmSetsPanelModel()
        self.view = view or FsmSetsPanelView()

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        # Consume interactions over panel; update hover/selection of items
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        if not getattr(self.model, 'visible', False):
            return False
        rect = getattr(self.view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                # Hover index based on simple row layout
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(self.model.items):
                    self.model.hovered_index = int(index)
                else:
                    self.model.hovered_index = None
                return True
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(self.model.items):
                    self.model.selected_index = int(index)
                return True
        if et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            if rect.collidepoint(pos):
                return True
        return False


__all__ = ["FsmSetsPanelController"]
