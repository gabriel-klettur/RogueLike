import os
import pygame
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_view import EntityPropertiesPanelView
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler

class EntityPropertiesPanelController:
    """Controller para el panel de propiedades de la entidad seleccionada."""
    def __init__(self, player_stats: dict[str, any], monsters: dict[str, any], font: pygame.font.Font):
        self.model = EntityPropertiesPanelModel(player_stats=player_stats, monsters=monsters)
        self.view = EntityPropertiesPanelView(font)
        self.event_handler = EntitiesPropertiesPanelEventHandler(self)
        # TextInput se maneja en el event handler

    def handle_event(self, event: pygame.event.Event) -> bool:
        return self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.view.draw(screen, self.model)
        if self.model.editing_property:
            for rect, key in self.model.property_entries:
                if key == self.model.editing_property:
                    prefix = f"{key}: "
                    x = rect.x + self.view.font.size(prefix)[0]
                    y = rect.y
                    self.event_handler.text_input.draw(screen, x, y)
                    break

    def _commit_edit(self) -> None:
        if not self.model.editing_property or not self.model.selected_id:
            return
        ent_id = self.model.selected_id
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
        self.model.focused_property = None