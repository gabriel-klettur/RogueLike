import os
import pygame
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_view import EntityPropertiesPanelView
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler


class EntityPropertiesPanelController:
    """
    Controller para el panel de propiedades de entidades.
    
    Responsable de:
    - Coordinar la vista, modelo y eventos del panel.
    - Gestionar la edición de propiedades (incluyendo validación y persistencia en JSON).
    """

    def __init__(self, player_stats: dict[str, any], monsters: dict[str, any], font: pygame.font.Font):
        """
        Inicializa el controller con datos y dependencias.

        Args:
            player_stats (dict): Diccionario con estadísticas del jugador.
            monsters (dict): Diccionario con datos de monstruos.
            font (pygame.font.Font): Fuente para renderizado de texto.
        """
        self.model = EntityPropertiesPanelModel(player_stats=player_stats, monsters=monsters)
        self.view = EntityPropertiesPanelView(font)
        self.event_handler = EntitiesPropertiesPanelEventHandler(self)

    # ----------------------------
    # MANEJO DE EVENTOS
    # ----------------------------
    def handle_event(self, event: pygame.event.Event) -> bool:
        """Delegación de eventos al EventHandler."""
        return self.event_handler.handle(event)

    # ----------------------------
    # RENDERIZADO DEL PANEL
    # ----------------------------
    def draw(self, screen: pygame.Surface) -> None:
        """Dibuja el panel y, si aplica, el input activo."""
        self.view.draw(screen, self.model)

        # Si hay propiedad en edición, renderizamos el TextInput
        if self.model.editing_property:
            for rect, key in self.model.property_entries:
                if key == self.model.editing_property:
                    prefix = f"{key}: "
                    x = rect.x + self.view.font.size(prefix)[0]
                    y = rect.y
                    self.event_handler.text_input.draw(screen, x, y)
                    break

    # ----------------------------
    # COMMIT DE CAMBIOS
    # ----------------------------
    def _commit_edit(self) -> None:
        """
        Aplica los cambios editados en la propiedad seleccionada y los persiste en JSON.
        """
        if not self.model.editing_property or not self.model.selected_id:
            return

        ent_id = self.model.selected_id
        key = self.model.editing_property
        new_text = self.model.editing_text

        # Cargar datos desde JSON correspondiente (jugador o monstruo)
        path, data, entry = self._load_entity_data(ent_id)

        # Conversión del valor editado al tipo original
        old_val = entry.get(key)
        converted = self._convert_value(new_text, old_val)

        # Actualizar en memoria y persistir en JSON
        entry[key] = converted
        self._save_entity_data(ent_id, entry, path, data)

        # Reset de estado de edición
        self._reset_edit_state()

    # ----------------------------
    # UTILIDADES PRIVADAS
    # ----------------------------
    def _load_entity_data(self, ent_id: str) -> tuple[str, dict, dict]:
        """
        Carga los datos JSON del jugador o monstruo correspondiente.

        Returns:
            (path, data, entry): Ruta al archivo, diccionario raíz, y la entrada de la entidad.
        """
        if ent_id in self.model.player_stats:
            path = os.path.join(os.getcwd(), "data", "entities", "players.json")
            root = load_from_json(path)
            data = root.get("PLAYER_STATS", {})
        else:
            path = os.path.join(os.getcwd(), "data", "entities", "monsters.json")
            data = load_from_json(path)

        entry = data.get(ent_id, {})
        return path, data, entry

    def _save_entity_data(self, ent_id: str, entry: dict, path: str, data: dict) -> None:
        """
        Persiste los cambios en el archivo JSON correspondiente y actualiza el modelo.
        """
        if ent_id in self.model.player_stats:
            data[ent_id] = entry
            save_to_json(path, "PLAYER_STATS", data)
            self.model.player_stats[ent_id] = entry
        else:
            save_to_json(path, ent_id, entry)
            self.model.monsters[ent_id] = entry

    def _convert_value(self, new_text: str, old_val: any) -> any:
        """
        Convierte el valor ingresado al tipo original (bool, int, float, str).
        Si falla la conversión, se mantiene como string.
        """
        try:
            if isinstance(old_val, bool):
                return new_text.lower() in ("true", "1", "yes")
            elif isinstance(old_val, int):
                return int(new_text)
            elif isinstance(old_val, float):
                return float(new_text)
            else:
                return new_text
        except ValueError:
            return new_text

    def _reset_edit_state(self) -> None:
        """Limpia las variables relacionadas con la edición."""
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
        self.model.focused_property = None
