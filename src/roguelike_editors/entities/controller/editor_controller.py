import pygame
pygame.font.init()
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from roguelike_editors.entities.model.editor_model import EntityEditorModel
from roguelike_editors.entities.view.editor_view import EntityEditorView
from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_editors.entities.events.entities_editor_events import EntitiesEditorEventHandler
import os


class EntityEditorController:
    """Controller para editor de entidades: jugador y monstruos."""
    def __init__(self, player_stats: dict[str, any], monsters: dict[str, any], assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.model = EntityEditorModel(player_stats=player_stats, monsters=monsters, assets=assets)
        self.view = EntityEditorView(assets, font)
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.view.text_input = self.text_input
        self.event_handler = EntitiesEditorEventHandler(self)

    def handle_event(self, event: pygame.event.Event) -> None:
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
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
        ent_id = self.model.selected_id or self.model.hovered_id
        if not ent_id:
            return
        key = self.model.editing_property
        new_text = self.model.editing_text
        # Determinar origen JSON y entrada
        if ent_id in self.model.player_stats:
            path = os.path.join(os.getcwd(), "data", "entities", "players.json")
            root = load_from_json(path)
            data = root.get("PLAYER_STATS", {})
            entry = data.get(ent_id, {})
        else:
            path = os.path.join(os.getcwd(), "data", "entities", "monsters.json")
            root = load_from_json(path)
            data = root
            entry = data.get(ent_id, {})

        old_val = entry.get(key)
        # Convertir tipo
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
        # Persistir cambios
        if ent_id in self.model.player_stats:
            data[ent_id] = entry
            save_to_json(path, "PLAYER_STATS", data)
            self.model.player_stats[ent_id] = entry
        else:
            save_to_json(path, ent_id, entry)
            self.model.monsters[ent_id] = entry

        # Reset edición
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
