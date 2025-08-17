from __future__ import annotations


class SpawnersManagerEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        model = controller.model
        if not getattr(model, 'visible', False):
            return False
        rect = getattr(controller.view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        if et == pygame.MOUSEWHEEL and rect.collidepoint(pos):
            # Scroll by row height (20px per wheel notch)
            dy = int(getattr(event, 'y', 0) or 0)
            current = int(getattr(model, 'scroll_offset', 0) or 0)
            # Positive y means wheel up -> move content down -> decrease offset
            new_offset = current - dy * 20
            # Clamp based on content height and viewport height
            content_h = int(getattr(controller.view, 'content_height', 0) or 0)
            panel = getattr(controller.view, 'panel_rect', None)
            if panel is not None:
                viewport_h = int(panel.height - 38)  # y_off=30, bottom padding=8
            else:
                viewport_h = 0
            max_scroll = max(0, content_h - max(0, viewport_h))
            model.scroll_offset = max(0, min(max_scroll, new_offset))
            return True
        # Consume mouse clicks inside the panel (future: editing)
        if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and rect.collidepoint(pos):
            return True
        return False


__all__ = ["SpawnersManagerEventHandler"]
