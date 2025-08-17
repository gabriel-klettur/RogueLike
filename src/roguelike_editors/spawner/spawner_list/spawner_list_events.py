from __future__ import annotations


class SpawnerListEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        model = controller.model
        view = controller.view
        if not getattr(model, 'visible', True):
            return False
        rect = getattr(view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()

        if et == pygame.MOUSEMOTION:
            if rect.collidepoint(pos):
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(model.items):
                    model.hovered_index = int(index)
                else:
                    model.hovered_index = None
                return True
            else:
                model.hovered_index = None
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                index = (pos[1] - rect.top - 28) // 20
                if 0 <= index < len(model.items):
                    model.selected_index = int(index)
                return True
        if et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            if rect.collidepoint(pos):
                return True
        return False


__all__ = ["SpawnerListEventHandler"]
