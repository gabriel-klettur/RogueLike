import pygame


class EntitiesAssetsPickerPanelEventHandler:
    """Event handler para el picker de assets de entidades."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view

    def handle(self, event: pygame.event.Event) -> bool:
        """Handle event: navigation, selection, and closing."""
        # Close on ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            self.controller.hide()
            return True
        # Mouse click
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Determine panel rect
            x, y = self.model.pos
            surf = self.view.fs_view.panel.surface
            w, h = surf.get_size()
            panel_rect = pygame.Rect(x, y, w, h)
            # Check entries first
            for rect, entry, idx in self.view.entry_rects:
                if rect.collidepoint(mx, my):
                    name, path, is_dir = entry
                    if is_dir:
                        self.model.fs_model.navigate(idx)
                    else:
                        if self.model.on_asset_chosen:
                            self.model.on_asset_chosen(self.model.key, path)
                        self.controller.hide()
                    return True
            # Click inside panel but not on entry: consume
            if panel_rect.collidepoint(mx, my):
                return True
            # Click outside: hide
            self.controller.hide()
            return True
        return False