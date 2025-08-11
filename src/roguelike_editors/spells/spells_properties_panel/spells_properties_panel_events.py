import pygame
import logging
from roguelike_editors.entities.entities_properties_panel.services.state_tabs_helpers import hit_test_state_tab

logger = logging.getLogger(__name__)


class SpellsPropertiesPanelEventHandler:
    """Event handler for Spells properties panel."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.text_input = controller.text_input
        self.dc_detector = controller.dc_detector

    def handle(self, event: pygame.event.Event) -> None:
        # Inline text input (only on 'properties' tab)
        if self.model.active_type_tab == 'properties' and self.text_input.active:
            if self.text_input.handle_event(event):
                self.model.editing_text = self.text_input.text
                self.model.editing_cursor = self.text_input.cursor
                if not self.text_input.active:
                    self.controller.commit_edit()
                return

        # Hover tracking for properties
        if event.type == pygame.MOUSEMOTION:
            if self.model.active_type_tab == 'properties':
                panel = getattr(self.model, 'content_view_rect', None)
                if panel and panel.collidepoint(event.pos):
                    new_hover = None
                    for rect, key in getattr(self.model, 'property_entries', []):
                        if rect.collidepoint(event.pos):
                            new_hover = key
                            break
                    self.model.hovered_property = new_hover
                else:
                    self.model.hovered_property = None
            else:
                self.model.hovered_property = None

        # Mouse wheel scroll
        if event.type == pygame.MOUSEWHEEL:
            if self.model.active_type_tab != 'properties':
                return
            panel = getattr(self.model, 'panel_rect', None)
            if panel:
                mx, my = pygame.mouse.get_pos()
                if panel.collidepoint(mx, my):
                    content_h = getattr(self.model, 'content_height', 0)
                    view_h = max(0, panel.h - 20)
                    if content_h > 0 and content_h <= view_h:
                        self.model.scroll_y = 0
                        return
                    max_scroll = max(0, content_h - view_h) if content_h > 0 else None
                    line_h = max(1, self.view.font.get_height() + 2)
                    delta = -event.y * (line_h * 3 // 2)
                    new_scroll = self.model.scroll_y + delta
                    if max_scroll is None:
                        self.model.scroll_y = max(0, new_scroll)
                    else:
                        self.model.scroll_y = max(0, min(new_scroll, max_scroll))
                    return

        # Legacy wheel buttons 4/5
        if event.type == pygame.MOUSEBUTTONDOWN and event.button in (4, 5):
            if self.model.active_type_tab != 'properties':
                return
            panel = getattr(self.model, 'panel_rect', None)
            if panel:
                mx, my = pygame.mouse.get_pos()
                if panel.collidepoint(mx, my):
                    content_h = getattr(self.model, 'content_height', 0)
                    view_h = max(0, panel.h - 20)
                    if content_h > 0 and content_h <= view_h:
                        self.model.scroll_y = 0
                        return
                    max_scroll = max(0, content_h - view_h) if content_h > 0 else None
                    line_h = max(1, self.view.font.get_height() + 2)
                    wheel_y = 1 if event.button == 4 else -1
                    delta = -wheel_y * (line_h * 3 // 2)
                    new_scroll = self.model.scroll_y + delta
                    if max_scroll is None:
                        self.model.scroll_y = max(0, new_scroll)
                    else:
                        self.model.scroll_y = max(0, min(new_scroll, max_scroll))
                    return

        # Mouse clicks: tabs, asset cell, properties
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Tabs
            tab_hit = None
            if getattr(self.model, 'type_tab_rects', None):
                tab_hit = hit_test_state_tab(self.model.type_tab_rects, (mx, my))
            if tab_hit:
                if tab_hit != self.model.active_type_tab:
                    # Reset edit state on tab change
                    self.model.focused_property = None
                    self.model.editing_property = None
                    self.model.hovered_property = None
                    if self.text_input.active:
                        self.text_input.deactivate()
                    self.model.active_type_tab = tab_hit
                return

            # Assets tab: open picker on double click
            if self.model.active_type_tab == 'assets':
                cell = getattr(self.model, 'asset_cell_rect', None)
                if cell and cell.collidepoint(mx, my):
                    if getattr(event, 'clicks', 1) >= 2 or self.dc_detector.is_double_click('asset_icon_cell'):
                        try:
                            self.controller.open_assets_picker()
                        except Exception:
                            logger.exception("[SpellsPropertiesPanel] open_assets_picker failed")
                    return
                # Click elsewhere inside the panel does nothing special
                panel = getattr(self.model, 'panel_rect', None)
                if panel and not panel.collidepoint(mx, my):
                    self.model.focused_property = None
                    self.model.editing_property = None
                    self.model.hovered_property = None
                return

            # Properties tab: click to focus/edit
            for rect, key in getattr(self.model, 'property_entries', []):
                if rect.collidepoint(mx, my):
                    if getattr(event, 'clicks', 1) >= 2 or self.dc_detector.is_double_click(key):
                        self.model.focused_property = key
                        self.model.editing_property = key
                        active_id = self.controller._selected_id or self.controller._hovered_id
                        data = self.controller._spells.get(active_id)
                        if data is not None:
                            initial = str(data.get(key, "")) if key != 'id' else str(active_id or "")
                        else:
                            initial = ""
                        self.model.editing_text = initial
                        self.model.editing_cursor = len(initial)
                        self.text_input.activate(initial)
                    else:
                        self.model.focused_property = key
                        self.model.hovered_property = key
                    return

            # Click outside panel clears focus/edit
            panel = getattr(self.model, 'panel_rect', None)
            if panel and not panel.collidepoint(mx, my):
                self.model.focused_property = None
                self.model.editing_property = None
                self.model.hovered_property = None
                return

        # No-op for other events
        return
