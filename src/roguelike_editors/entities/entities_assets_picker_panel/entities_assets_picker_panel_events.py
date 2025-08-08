import pygame
import logging
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector

import logging
logger = logging.getLogger(__name__)


class EntitiesAssetsPickerPanelEventHandler:
    """Event handler para el picker de assets de entidades."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.dc_detector = DoubleClickDetector()

    def handle(self, event: pygame.event.Event) -> bool:
        """Handle event: navigation, selection, and closing."""
        # Close on ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            self.controller.hide()
            return True
        # Mouse click
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            logger.debug(f" Click at pos={mx},{my}, selected={self.model.fs_model.selected}")
            # Determine panel rect (include footer if available)
            if self.model.panel_rect is not None:
                panel_rect = self.model.panel_rect
            else:
                x, y = self.model.pos
                surf = self.view.fs_view.panel.surface
                w, h = surf.get_size()
                panel_rect = pygame.Rect(x, y, w, h)
            # Check entries first
            for rect, entry, idx in self.view.entry_rects:
                if rect.collidepoint(mx, my):
                    name, path, is_dir = entry
                    if is_dir:
                        # navigate on double-click
                        if self.dc_detector.is_double_click(idx):
                            self.model.fs_model.navigate(idx)
                        else:
                            # single-click: highlight only
                            self.model.fs_model.selected = entry[1]
                    else:
                        # select asset on double-click without closing panel
                        if self.dc_detector.is_double_click(idx):
                            if self.model.on_asset_chosen:
                                logger.debug(f" Invoking on_asset_chosen callback for key={self.model.key}, path={path}")
                                self.model.on_asset_chosen(self.model.key, path)


                        else:
                            # single-click: highlight only
                            self.model.fs_model.selected = entry[1]
                    return True
            # Click inside panel but not on entry: consume
            if panel_rect.collidepoint(mx, my):
                return True
            # Click outside: hide
            self.controller.hide()
            return True
        return False