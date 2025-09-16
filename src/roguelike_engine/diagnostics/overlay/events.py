import pygame
from .model import DiagnosticsOverlayModel
from .view import DiagnosticsOverlayView

def handle_event(model: DiagnosticsOverlayModel, view: DiagnosticsOverlayView, event: pygame.event.Event) -> bool:
    """Route mouse/keyboard events for the DiagnosticsOverlay.

    Returns True if the overlay consumed the event, False otherwise.
    """
    et = event.type
    # Scroll wheel moves panel content
    if et == pygame.MOUSEWHEEL:
        # Ctrl + wheel => cambiar página (si paginación activa y el ratón está sobre el panel)
        mods = pygame.key.get_mods()
        if getattr(model, 'paging_enabled', False) and model.panel_rect and model.panel_rect.collidepoint(pygame.mouse.get_pos()) and (mods & pygame.KMOD_CTRL):
            if event.y > 0:
                # wheel up -> página anterior
                model.page_index = max(0, model.page_index - 1)
            elif event.y < 0:
                # wheel down -> página siguiente
                model.page_index = min(max(0, model.total_pages - 1), model.page_index + 1)
            model.scroll_offset = 0
            model.reset_panel()
            return True
        # Scroll normal => desplazar dentro de la página
        model.scroll_offset = max(0, model.scroll_offset - event.y * model.scroll_speed)
        # Clamp to content height if possible
        try:
            if model.panel_surf is not None and model.panel_rect is not None:
                content_h = model.panel_surf.get_height()
                # Estimate visible height based on screen area available below panel top
                screen = pygame.display.get_surface()
                if screen is not None:
                    screen_h = screen.get_height()
                    visible_h = max(0, min(content_h, screen_h - model.panel_rect.top))
                else:
                    # Fallback: at least one line
                    visible_h = view.line_height(model)
                max_scroll = max(0, content_h - visible_h)
                if model.scroll_offset > max_scroll:
                    model.scroll_offset = max_scroll
        except Exception:
            # Be conservative if any issue occurs
            pass
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
                    # Persist collapsed state across sessions
                    try:
                        model.save_persisted_state()
                    except Exception:
                        pass
                    return True
    # Navegación por teclas de paginación cuando el cursor está sobre el panel
    if et == pygame.KEYDOWN and model.panel_rect and model.panel_rect.collidepoint(pygame.mouse.get_pos()):
        if getattr(model, 'paging_enabled', False):
            if event.key in (pygame.K_PAGEUP, pygame.K_UP):
                model.page_index = max(0, model.page_index - 1)
                model.scroll_offset = 0
                model.reset_panel()
                return True
            if event.key in (pygame.K_PAGEDOWN, pygame.K_DOWN):
                model.page_index = min(max(0, model.total_pages - 1), model.page_index + 1)
                model.scroll_offset = 0
                model.reset_panel()
                return True
            if event.key in (pygame.K_HOME,):
                model.page_index = 0
                model.scroll_offset = 0
                model.reset_panel()
                return True
            if event.key in (pygame.K_END,):
                model.page_index = max(0, model.total_pages - 1)
                model.scroll_offset = 0
                model.reset_panel()
                return True
    return False
