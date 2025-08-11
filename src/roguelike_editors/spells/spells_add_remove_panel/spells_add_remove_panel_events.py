"""
Spells Add/Remove panel events.
"""

import os
import pygame
from roguelike_ui.services.json_persistence import save_to_json
from roguelike_engine.utils.loader import load_image


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

