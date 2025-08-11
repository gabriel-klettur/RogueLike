import pygame

from .model import DebugOverlayModel
from .view import DebugOverlayView


def handle_event(model: DebugOverlayModel, view: DebugOverlayView, event) -> bool:
    if event.type == pygame.MOUSEWHEEL:
        if event.y > 0:  # Scroll up
            model.scroll_offset = max(0, model.scroll_offset - model.scroll_speed)
        elif event.y < 0:  # Scroll down
            model.scroll_offset += model.scroll_speed
        return True

    if event.type == pygame.MOUSEBUTTONDOWN:
        # Some platforms also send wheel as button 4/5
        if event.button == 4:
            model.scroll_offset = max(0, model.scroll_offset - model.scroll_speed)
            return True
        if event.button == 5:
            model.scroll_offset += model.scroll_speed
            return True
        if event.button == 1 and model.panel_rect and model.panel_rect.collidepoint(event.pos):
            line_h = view.line_height(model)
            local_y = event.pos[1] - model.panel_rect.top + model.scroll_offset
            index = local_y // line_h
            if 0 <= index < len(model.line_keys):
                key = model.line_keys[index]
                if key:
                    if key.endswith(':'):
                        root = key[:-1].strip()
                        group = root.split('.')[0]
                    else:
                        group = key.split('.')[0]
                    if group in model.collapsed_groups:
                        model.collapsed_groups.remove(group)
                    else:
                        model.collapsed_groups.add(group)
                    model.reset_panel()
                    return True
    return False
