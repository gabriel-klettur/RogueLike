import pygame
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector

class AssetsGridPanelEventHandler:
    """Event handler for the assets grid panel."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.dc_detector = DoubleClickDetector()

    def handle(self, event: pygame.event.Event) -> bool:
        """Handle an event and return whether it was consumed."""
        if event.type == pygame.MOUSEMOTION:
            return self._handle_hover(event)
        elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # detect click then double-click
            for rect, key in getattr(self.model, 'asset_cell_entries', []):
                if rect.collidepoint(mx, my):
                    print(f"Clicked asset cell {key}")
                    if self.dc_detector.is_double_click(key):
                        print(f"Double-click detected for asset cell {key}")
                        prop_ctrl = self.controller.parent_controller
                        editor_ctrl = prop_ctrl.editor_controller
                        # Position assets picker under the entities picker panel
                        picker_rect = editor_ctrl.picker_controller.model.panel_rect
                        if picker_rect:
                            x0, y0, w0 = picker_rect.x, picker_rect.bottom, picker_rect.width
                        else:
                            x0, y0, w0 = rect.x, rect.bottom, rect.width
                        print(f"Opening assets picker for cell {key} at {(x0, y0)} width {w0}")
                        prop_ctrl.assets_picker_controller.show(key, x0, y0, w0, prop_ctrl._on_asset_chosen)
                        return True
                    break
            return self._handle_click(event)
        return False

    def _handle_hover(self, event: pygame.event.Event) -> bool:
        """Handle mouse motion to detect hover over asset cells."""
        if not hasattr(self.model, 'asset_cell_entries') or not self.model.asset_cell_entries:
            return False
            
        mx, my = event.pos
        hovered = None
        
        for rect, key in self.model.asset_cell_entries:
            if rect.collidepoint(mx, my):
                hovered = key
                break
                
        self.model.hovered_asset_cell = hovered
        return hovered is not None

    def _handle_click(self, event: pygame.event.Event) -> bool:
        """Handle mouse click to select an asset cell and open assets picker on double-click."""
        if not hasattr(self.model, 'asset_cell_entries') or not self.model.asset_cell_entries:
            return False

        mx, my = event.pos
        clicked = False
        for rect, key in self.model.asset_cell_entries:
            if rect.collidepoint(mx, my):
                self.model.selected_asset_cell = key  # Set selected asset cell
                print(f"Clicked asset cell {key}")
                # detect double-click
                if self.dc_detector.is_double_click(key):
                    print(f"Double-click detected for asset cell {key}")
                    prop_ctrl = self.controller.parent_controller
                    editor_ctrl = prop_ctrl.editor_controller
                    # Position assets picker under the entities picker panel
                    picker_rect = editor_ctrl.picker_controller.model.panel_rect
                    if picker_rect:
                        x0, y0, w0 = picker_rect.x, picker_rect.bottom, picker_rect.width
                    else:
                        x0, y0, w0 = rect.x, rect.bottom, rect.width
                    print(f"Showing assets picker for cell {key} at {(x0, y0)} size {w0}")
                    prop_ctrl.assets_picker_controller.show(key, x0, y0, w0, prop_ctrl._on_asset_chosen)
                clicked = True
                break

        return clicked
