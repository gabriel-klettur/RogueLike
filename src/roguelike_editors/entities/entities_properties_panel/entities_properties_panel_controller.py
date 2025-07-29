import os
import pygame
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from pathlib import Path
from roguelike_engine.config.config import ASSETS_DIR
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_view import EntityPropertiesPanelView
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_controller import AssetsGridPanelController
from roguelike_editors.entities.entities_properties_panel.entities_state_tabs.entities_state_tabs_controller import EntitiesStateTabsController
from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_controller import EntitiesTypeAssetsController
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_controller import EntitiesAssetsPickerPanelController


class EntityPropertiesPanelController:
    """
    Controller para el panel de propiedades de entidades.
    
    Responsable de:
    - Coordinar la vista, modelo y eventos del panel.
    - Gestionar la edición de propiedades (incluyendo validación y persistencia en JSON).
    """

    def __init__(self, editor_controller, player_stats: dict[str, any], monsters: dict[str, any], player_assets: dict[str, any], font: pygame.font.Font):
        """
        Inicializa el controller con datos y dependencias.

        Args:
            player_stats (dict): Diccionario con estadísticas del jugador.
            monsters (dict): Diccionario con datos de monstruos.
            font (pygame.font.Font): Fuente para renderizado de texto.
        """
        self.editor_controller = editor_controller
        self.model = EntityPropertiesPanelModel(player_stats=player_stats, player_assets=player_assets, monsters=monsters)
        self.view = EntityPropertiesPanelView(font)
        self.event_handler = EntitiesPropertiesPanelEventHandler(self)
        self.grid_controller = AssetsGridPanelController(self, font)
        self.view.grid_controller = self.grid_controller
        # Controller de pestañas de tipo ('properties'/'assets')
        self.type_assets_controller = EntitiesTypeAssetsController(self.model, font)
        self.view.type_assets_controller = self.type_assets_controller

        # Controller de pestañas de estado
        self.state_tabs_controller = EntitiesStateTabsController(self.model, font)
        self.view.state_tabs_controller = self.state_tabs_controller
        # Pasar controller de tabs de estado al grid view para seleccionar assets
        self.grid_controller.view.state_tabs_controller = self.state_tabs_controller
        # Assets picker panel
        self.assets_picker_controller = EntitiesAssetsPickerPanelController()
        self.view.assets_picker_controller = self.assets_picker_controller

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
        # Draw assets picker if visible
        if self.assets_picker_controller.model.visible:
            self.assets_picker_controller.draw(screen)

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
    def _on_asset_chosen(self, cell_key: str, path):
        """
        Callback when asset is chosen: update entity property and persist to JSON.
        """
        ent_id = self.model.selected_id
        if not ent_id:
            return
        # update JSON and model
        json_path, data, entry = self._load_entity_data(ent_id)
        # determine state and direction from cell_key
        # compute relative asset path
        abs_path = Path(path).resolve()
        assets_root = Path(ASSETS_DIR).resolve()
        try:
            rel = abs_path.relative_to(assets_root)
            rel_path = f"assets/{rel.as_posix()}"
        except ValueError:
            rel_path = str(path).replace("\\", "/")

        parts = cell_key.split("_")
        if len(parts) == 3:
            _, state, direction = parts
            # ensure nested structure exists
            sprites = entry.setdefault("sprites", {})
            assets = sprites.setdefault("assets", {})
            state_assets = assets.setdefault(state, {})
            state_assets[direction] = rel_path
        else:
            # fallback: flat property
            entry[cell_key] = rel_path
        self._save_entity_data(ent_id, entry, json_path, data)
        # Refresh monster asset caches and update ECS entities immediately
        try:
            from roguelike_game.factories.monster.config import reload_monster_defs
            from roguelike_game.factories.monster import cache as monster_cache
            # Reload definitions and clear caches for this type
            reload_monster_defs()
            monster_cache._loaded_variants.discard(ent_id)
            monster_cache._SPRITE_SURFACES.pop(ent_id, None)
            monster_cache._DEATH_SURFACES.pop(ent_id, None)
            monster_cache.load_caches_for([ent_id])
            # Update existing ECS entities of this monster type
            ecs_world = self.editor_controller.game.ecs.ecs_world
            idents = ecs_world.components.get('Identity', {})
            sprites = ecs_world.components.get('Sprite', {})
            animators = ecs_world.components.get('Animator', {})
            for eid, identity in idents.items():
                if identity.name.lower() == ent_id:
                    base_map = monster_cache._SPRITE_SURFACES.get(ent_id, {})
                    # Update sprite image to 'down' frame
                    down_surf = base_map.get('down')
                    if down_surf and eid in sprites:
                        raw = down_surf.copy() if hasattr(down_surf, 'copy') else down_surf
                        sprites[eid].image = raw
                    # Update animation frames
                    if eid in animators:
                        new_anims = {state: [surf.copy() if hasattr(surf, 'copy') else surf]
                                     for state, surf in base_map.items()}
                        animators[eid].animations = new_anims
        except Exception:
            pass
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
