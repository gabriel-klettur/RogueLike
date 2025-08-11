"""
Spells toolbar MVC implementation (model, view, events, controller).
We keep this in a single module to avoid conflicts with existing empty stubs.
"""
import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.services.constants import UI_MARGIN


class SpellsToolBarPanelModel:
    """Data model for Spells toolbar."""
    def __init__(self):
        # Center the main toggle 'spells_on_map' between undo/redo
        self.tools = ['undo', 'spells_on_map', 'redo']
        self.active_tool: str | None = None


class SpellsToolBarPanelView:
    """Toolbar view for Spells, positioned under the title bar."""
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        # Default position; will be updated on render based on title
        self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8
        icon_paths = {
            'spells_on_map': 'assets/ui/spells_on_map_icon.png',
            'undo': 'assets/ui/undo.png',
            'redo': 'assets/ui/redo.png',
        }
        self.icons = {}
        for tool in self.model.tools:
            path = icon_paths.get(tool)
            if path:
                try:
                    img = load_image(path, scale=(self.size, self.size))
                except Exception:
                    img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                    img.fill((100, 100, 100, 150))
            else:
                img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                img.fill((100, 100, 100, 150))
            self.icons[tool] = img

        self.widget = ToolbarView(
            controller=self.controller,
            items=self.model.tools,
            icons=self.icons,
            x=self.x,
            y=self.y,
            size=self.size,
            padding=self.padding,
            name='SpellsToolBarPanel',
        )

    def render(self, screen: pygame.Surface):
        # Center group horizontally
        try:
            sw = screen.get_width()
        except Exception:
            sw = 1280
        total_w = self.size * len(self.model.tools) + self.padding * (len(self.model.tools) - 1)
        self.widget.x = (sw - total_w) // 2
        # Position under the title bar if available
        title_widget = getattr(getattr(self.controller, 'title_controller', None), 'view', None)
        title_widget = getattr(title_widget, 'widget', None)
        if title_widget is not None:
            title_text = title_widget.text or ""
            text_surf = title_widget.font.render(title_text, True, title_widget.text_color)
            bg_h = text_surf.get_height() + title_widget.padding_y * 2
            self.widget.y = title_widget.y + bg_h + UI_MARGIN
        else:
            # Fallback: use picker view's title_rect from editor controller
            picker_view = getattr(self.controller, 'picker_controller', None)
            if picker_view is None:
                picker_view = getattr(self.controller, 'editor_controller', None)
            picker_view = getattr(picker_view, 'view', None)
            title_rect = getattr(picker_view, 'title_rect', None)
            if title_rect is not None:
                self.widget.y = title_rect.bottom + UI_MARGIN
            else:
                self.widget.y = self.y
        self.widget.render(screen)

    def handle_event(self, event):
        return self.widget.handle_event(event)


class SpellsToolBarPanelEventHandler:
    """Event handler for Spells toolbar."""
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event) -> bool:
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            toolbar_view = getattr(self.controller, 'spells_toolbar_view', None)
            widget = getattr(toolbar_view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}
            # Undo (placeholder)
            rect = icon_rects.get('undo')
            if rect and rect.collidepoint(pos):
                return True
            # Redo (placeholder)
            rect = icon_rects.get('redo')
            if rect and rect.collidepoint(pos):
                return True
            # Main toggle
            rect = icon_rects.get('spells_on_map')
            if rect and rect.collidepoint(pos):
                if self.model.active_tool == 'spells_on_map':
                    # Deactivate: hide picker and add/remove panel
                    self.model.active_tool = None
                    self.controller.model.picker_visible = False
                    # Always exit delete mode when hiding toolbar/picker
                    if hasattr(self.controller.model, 'delete_mode_active'):
                        self.controller.model.delete_mode_active = False
                    arm = getattr(self.controller, 'spells_add_remove_model', None)
                    if arm is not None:
                        arm.visible = False
                        arm.active_tool = None
                else:
                    # Activate: show picker and add/remove panel
                    self.model.active_tool = 'spells_on_map'
                    self.controller.model.picker_visible = True
                    # Exit delete mode on activation as well for a clean state
                    if hasattr(self.controller.model, 'delete_mode_active'):
                        self.controller.model.delete_mode_active = False
                    arm = getattr(self.controller, 'spells_add_remove_model', None)
                    if arm is not None:
                        arm.visible = True
                        arm.active_tool = None
                return True
        return False


class SpellsToolBarPanelController:
    """Controller wrapper to coordinate toolbar MVC."""
    def __init__(self, editor_controller, model, view, event_handler):
        self.editor_controller = editor_controller
        self.model = model
        self.view = view
        self.event_handler = event_handler
        # For positioning under title
        self.title_controller = getattr(editor_controller, 'title_controller', None)
        # Expose picker ref for fallback positioning
        self.picker_controller = editor_controller
        # Optional link to add/remove
        self.add_remove_controller = None

    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

    def render(self, screen):
        if hasattr(self.view, 'render'):
            self.view.render(screen)

    def handle_event(self, event):
        if hasattr(self.view, 'handle_event') and self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event) if hasattr(self.event_handler, 'handle_event') else False
