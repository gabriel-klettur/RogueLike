import pygame
pygame.font.init()
import os
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from roguelike_editors.spells.spells_picker_panel.spells_editor_model import SpellEditorModel
from roguelike_editors.spells.spells_picker_panel.spells_editor_view import SpellEditorView
from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_editors.spells.spells_picker_panel.spells_editor_events import SpellEditorEventHandler

class SpellEditorController:
    """Controller for Spell Editor UI."""
    def __init__(self, spells: dict[str, any], assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.model = SpellEditorModel(spells=spells.copy(), assets=assets)
        self.view = SpellEditorView(assets, font)
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.view.text_input = self.text_input
        self.event_handler = SpellEditorEventHandler(self)

    def handle_event(self, event: pygame.event.Event) -> None:
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.view.draw(screen, self.model)
        if self.model.editing_property:
            for rect, key in self.model.property_entries:
                if key == self.model.editing_property:
                    prefix = f"{key}: "
                    x = rect.x + self.view.font.size(prefix)[0]
                    y = rect.y
                    self.text_input.draw(screen, x, y)
                    break

    def _commit_edit(self) -> None:
        if not self.model.editing_property:
            return
        sid = self.model.selected_id or self.model.hovered_id
        if not sid:
            return
        key = self.model.editing_property
        new_text = self.model.editing_text
        # JSON path
        path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
        root = load_from_json(path)
        entry = root.get(sid, {})
        old_val = entry.get(key)
        # Convert type
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
        entry[key] = converted
        # Persist changes
        save_to_json(path, sid, entry)
        # Update model
        self.model.spells[sid] = entry
        # Reset editing
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
