"""
Spells Add/Remove panel MVC. Two tools: add_spell (clone selected on click) and remove_spell (delete on click).
"""
import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image
from roguelike_ui.services.json_persistence import save_to_json
import os
from roguelike_editors.entities.services.constants import UI_MARGIN


class SpellsAddRemovePanelModel:
    def __init__(self):
        self.tools = ['add_spell', 'remove_spell']
        self.active_tool: str | None = None
        self.visible: bool = False


class SpellsAddRemovePanelView:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        self.size = 64
        self.padding = 8
        self.x, self.y = 10, 80
        icon_paths = {
            'add_spell': 'assets/ui/add_spell.png',
            'remove_spell': 'assets/ui/remove_spell.png',
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
            name='SpellsAddRemovePanel',
        )

    def render(self, screen):
        if not getattr(self.model, 'visible', False):
            return
        # Position to the right of main toolbar if available
        toolbar_view = getattr(self.controller, 'spells_toolbar_view', None)
        if toolbar_view is not None:
            tb_widget = toolbar_view.widget
            panel_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
            panel_w, _ = tb_widget.panel.surface.get_size()
            self.widget.x = panel_pos[0] + panel_w + UI_MARGIN
            self.widget.y = panel_pos[1]
        self.widget.render(screen)
        # Blink border for active modes
        now = pygame.time.get_ticks()
        if (now // 500) % 2 == 0:
            if self.model.active_tool == 'add_spell':
                rect = self.widget.icon_rects.get('add_spell')
                if rect:
                    pygame.draw.rect(screen, (255, 255, 0), rect.inflate(6, 6), 3)
            if self.model.active_tool == 'remove_spell':
                rect = self.widget.icon_rects.get('remove_spell')
                if rect:
                    pygame.draw.rect(screen, (255, 0, 0), rect.inflate(6, 6), 3)

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        return self.widget.handle_event(event)


class SpellsAddRemovePanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            toolbar_view = getattr(self.controller, 'spells_add_remove_view', None)
            widget = getattr(toolbar_view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}
            # Add
            rect_add = icon_rects.get('add_spell')
            if rect_add and rect_add.collidepoint(pos):
                # Determine base entry to clone
                sid_base = self.controller.model.selected_id
                if not sid_base and self.controller.model.spells:
                    sid_base = next(iter(self.controller.model.spells.keys()))
                base = dict(self.controller.model.spells.get(sid_base, {})) if sid_base else {
                    'name': 'New Spell',
                    'sprite': '',
                }
                # Generate unique id
                def unique_id(prefix: str) -> str:
                    i = 1
                    cand = prefix
                    existing = self.controller.model.spells
                    while cand in existing:
                        cand = f"{prefix}_{i}"
                        i += 1
                    return cand
                pref = (sid_base + '_copy') if sid_base else 'new_spell'
                new_id = unique_id(pref)
                # Persist
                path = os.path.join(os.getcwd(), 'data', 'spells', 'spells.json')
                save_to_json(path, new_id, base)
                # Update model
                self.controller.model.spells[new_id] = base
                sprite_path = base.get('sprite')
                if sprite_path:
                    try:
                        self.controller.model.assets[new_id] = load_image(sprite_path)
                    except Exception:
                        pass
                self.controller.model.selected_id = new_id
                self.controller.model.picker_visible = True
                # Exit add mode visual
                self.model.active_tool = None
                return True
            # Remove toggle
            rect_del = icon_rects.get('remove_spell')
            if rect_del and rect_del.collidepoint(pos):
                # Toggle active tool
                if self.model.active_tool == 'remove_spell':
                    self.model.active_tool = None
                    self.controller.model.delete_mode_active = False
                else:
                    self.model.active_tool = 'remove_spell'
                    self.controller.model.delete_mode_active = True
                return True
        return False


class SpellsAddRemovePanelController:
    def __init__(self, editor_controller, model, view, event_handler):
        self.editor_controller = editor_controller
        self.model = model
        self.view = view
        self.event_handler = event_handler

    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

    def render(self, screen):
        if hasattr(self.view, 'render'):
            self.view.render(screen)

    def handle_event(self, event):
        if hasattr(self.view, 'handle_event') and self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event) if hasattr(self.event_handler, 'handle_event') else False
