import os
import json
import pygame
from typing import Any, Dict, Optional

from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_ui.services.json_persistence import save_to_json, load_from_json

from .spells_properties_panel_models import SpellsPropertiesPanelModel
from .spells_properties_panel_view import SpellsPropertiesPanelView
from .spells_properties_panel_events import SpellsPropertiesPanelEventHandler
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_controller import (
    EntitiesAssetsPickerPanelController,
)
from roguelike_engine.utils.loader import load_image

import logging
logger = logging.getLogger(__name__)


class SpellsPropertiesPanelController:
    """Controller for the Spells Properties panel."""

    def __init__(self, spells: Dict[str, Any], font: pygame.font.Font):
        self.model = SpellsPropertiesPanelModel()
        self.view = SpellsPropertiesPanelView(font)
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.event_handler = SpellsPropertiesPanelEventHandler(self)
        self.assets_picker = EntitiesAssetsPickerPanelController()

        # External references
        self.editor_controller = None
        self._spells: Dict[str, Any] = spells
        self._selected_id: Optional[str] = None
        self._hovered_id: Optional[str] = None

        # Callbacks provided by editor controller
        # get_assets_anchor_rect() -> pygame.Rect | None
        self.get_assets_anchor_rect = None
        # on_asset_changed(spell_id: str, new_asset_path: str) -> None
        self.on_asset_changed = None
        # on_after_commit_edit(key, old_id, new_id, value)
        self.on_after_commit_edit = None

    # --- External linking ---
    def set_spells(self, spells: Dict[str, Any]):
        self._spells = spells

    def set_active_ids(self, selected_id: Optional[str], hovered_id: Optional[str]):
        self._selected_id = selected_id
        self._hovered_id = hovered_id

    def update_context(self, spells: Dict[str, Any], selected_id: Optional[str], hovered_id: Optional[str]):
        self._spells = spells
        self._selected_id = selected_id
        self._hovered_id = hovered_id

    # --- UI Loop ---
    def handle_event(self, event: pygame.event.Event) -> bool:
        try:
            if self.assets_picker.handle_event(event):
                return True
        except Exception:
            pass
        prev_state = (
            self.model.editing_property,
            self.model.editing_text,
            self.model.scroll_y,
            self.model.active_type_tab,
        )
        self.event_handler.handle(event)
        new_state = (
            self.model.editing_property,
            self.model.editing_text,
            self.model.scroll_y,
            self.model.active_type_tab,
        )
        return prev_state != new_state

    def draw(self, screen: pygame.Surface, title_rect: Optional[pygame.Rect] = None) -> None:
        active_id = self._selected_id or self._hovered_id
        self.view.draw(screen, self.model, self._spells, active_id, title_rect)
        # Inline caret
        if self.model.editing_property and self.text_input.active:
            for rect_prop, key_prop in self.model.property_entries:
                if key_prop == self.model.editing_property:
                    prefix = f"{key_prop}: "
                    x = rect_prop.x + self.view.font.size(prefix)[0]
                    y = rect_prop.y
                    self.text_input.draw(screen, x, y)
                    break
        # Draw assets picker if visible
        try:
            self.assets_picker.draw(screen)
        except Exception:
            pass

    # --- Actions ---
    def start_inline_edit(self, prop_key: Optional[str] = None) -> None:
        active_id = self._selected_id or self._hovered_id
        if not active_id or active_id not in self._spells:
            return
        data = self._spells.get(active_id) or {}
        # Pick first available key if not provided
        key_to_edit = prop_key
        if key_to_edit is None:
            for k, v in data.items():
                if v is None:
                    continue
                key_to_edit = k
                break
        if not key_to_edit:
            return
        self.model.focused_property = key_to_edit
        self.model.editing_property = key_to_edit
        initial = str(data.get(key_to_edit, "")) if key_to_edit != 'id' else str(active_id or "")
        self.model.editing_text = initial
        self.model.editing_cursor = len(initial)
        self.text_input.activate(initial)

    def commit_edit(self) -> None:
        if not self.model.editing_property:
            return
        spell_id = self._selected_id or self._hovered_id
        if spell_id and spell_id in self._spells:
            key = self.model.editing_property
            new_text = self.model.editing_text
            # Obtain current entry and old type
            path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
            data_json = load_from_json(path)
            entry = data_json.get(spell_id, {}).copy()
            old_val = entry.get(key)
            try:
                if isinstance(old_val, bool):
                    converted = new_text.lower() in ("true", "1", "yes")
                elif isinstance(old_val, int):
                    converted = int(new_text)
                elif isinstance(old_val, float):
                    converted = float(new_text)
                else:
                    converted = new_text
            except ValueError:
                converted = new_text

            if key == 'id':
                new_id = str(converted)
                if not new_id:
                    logger.warning("[SpellsPropertiesPanel] Empty id not allowed; ignoring change")
                elif new_id == spell_id:
                    entry['id'] = new_id
                    save_to_json(path, spell_id, entry)
                    try:
                        if callable(self.on_after_commit_edit):
                            self.on_after_commit_edit('id', spell_id, spell_id, new_id)
                    except Exception:
                        logger.exception("[SpellsPropertiesPanel] on_after_commit_edit callback failed")
                else:
                    if new_id in data_json:
                        logger.error(f"[SpellsPropertiesPanel] Cannot rename id: '{new_id}' already exists")
                    else:
                        # Move JSON entry
                        entry['id'] = new_id
                        data_json[new_id] = entry
                        if spell_id in data_json:
                            del data_json[spell_id]
                        try:
                            with open(path, 'w', encoding='utf-8') as f:
                                json.dump(data_json, f, ensure_ascii=False, indent=2)
                        except Exception as e:
                            logger.exception(f"[SpellsPropertiesPanel] Failed to rewrite spells JSON on id rename: {e}")
                        # Update in-memory map and selection
                        try:
                            self._spells[new_id] = self._spells.pop(spell_id)
                        except Exception:
                            pass
                        if self._selected_id == spell_id:
                            self._selected_id = new_id
                        if self._hovered_id == spell_id:
                            self._hovered_id = new_id
                        try:
                            if callable(self.on_after_commit_edit):
                                self.on_after_commit_edit('id', spell_id, new_id, new_id)
                        except Exception:
                            logger.exception("[SpellsPropertiesPanel] on_after_commit_edit callback failed")
            else:
                entry[key] = converted
                save_to_json(path, spell_id, entry)
                # Update in-memory
                self._spells[spell_id] = entry
                # If sprite changed, refresh asset
                if key == 'sprite' and isinstance(converted, str):
                    try:
                        img = load_image(converted)
                        if callable(self.on_asset_changed):
                            self.on_asset_changed(spell_id, converted)
                    except Exception:
                        logger.exception("[SpellsPropertiesPanel] failed to load sprite '%s'", converted)
                try:
                    if callable(self.on_after_commit_edit):
                        self.on_after_commit_edit(key, spell_id, None, converted)
                except Exception:
                    logger.exception("[SpellsPropertiesPanel] on_after_commit_edit callback failed")
        # Reset editing state
        self.text_input.deactivate()
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0

    def open_assets_picker(self) -> None:
        """Open the assets picker anchored near the asset cell."""
        active_id = self._selected_id or self._hovered_id
        if not active_id:
            return
        cell = getattr(self.model, 'asset_cell_rect', None)
        anchor = None
        try:
            if callable(self.get_assets_anchor_rect):
                anchor = self.get_assets_anchor_rect()
        except Exception:
            anchor = None
        x = (cell.x if cell else (anchor.x if anchor else 20))
        y = (cell.bottom if cell else (anchor.bottom if anchor else 80))
        width = (cell.w if cell else (anchor.w if anchor else 320))

        def _on_chosen(asset_path: str):
            # Persist sprite path and notify
            path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
            data_json = load_from_json(path)
            entry = data_json.get(active_id, {}).copy()
            entry['sprite'] = asset_path
            save_to_json(path, active_id, entry)
            self._spells[active_id] = entry
            try:
                if callable(self.on_asset_changed):
                    self.on_asset_changed(active_id, asset_path)
            except Exception:
                logger.exception("[SpellsPropertiesPanel] on_asset_changed callback failed")
            self.assets_picker.hide()

        self.assets_picker.show(key='sprite', x=x, y=y, width=width, callback=_on_chosen, label_provider=None)
