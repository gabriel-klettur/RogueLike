import pygame

from .model import DiagnosticsOverlayModel
from .view import DiagnosticsOverlayView


def handle_event(model: DiagnosticsOverlayModel, view: DiagnosticsOverlayView, event) -> bool:
    et = event.type
    # Scroll wheel moves panel content
    if et == pygame.MOUSEWHEEL:
        model.scroll_offset = max(0, model.scroll_offset - event.y * model.scroll_speed)
        return True
    # Click toggles collapse/expand per group
    if et == pygame.MOUSEBUTTONDOWN and event.button == 1:
        lx, ly = event.pos
        if model.panel_rect and model.panel_rect.collidepoint((lx, ly)):
            local_y = ly - model.panel_rect.top + model.scroll_offset
            line_h = view.line_height(model)
            index = local_y // line_h
            if 0 <= index < len(model.line_keys):
                key = model.line_keys[index]
                if key.endswith(':'):
                    # Use group id only to toggle
                    group_id = key[:-1]
                    if group_id in model.collapsed_groups:
                        model.collapsed_groups.remove(group_id)
                    else:
                        model.collapsed_groups.add(group_id)
                    model.reset_panel()
                    return True
    return False
