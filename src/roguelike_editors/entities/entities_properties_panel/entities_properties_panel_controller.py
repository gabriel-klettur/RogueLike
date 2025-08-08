import os
import json
import pygame
import logging
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from pathlib import Path
from roguelike_engine.config.config import ASSETS_DIR
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_view import EntityPropertiesPanelView
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_controller import AssetsGridPanelController
from roguelike_editors.entities.entities_properties_panel.entities_state_tabs.entities_state_tabs_controller import EntitiesStateTabsController
from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_controller import EntitiesTypeAssetsController
from roguelike_editors.entities.entities_properties_panel.entities_assets_subtabs.entities_assets_subtabs_controller import EntitiesAssetsSubTabsController
from roguelike_editors.entities.entities_properties_panel.services.assets_constants import (
    TYPE_TAB_ASSETS,
    SUBTAB_SET,
    SUBTAB_NO_SET,
)
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_controller import EntitiesAssetsPickerPanelController
import importlib
import roguelike_game.config.players_config as pc
from roguelike_game.factories.monster.config import reload_monster_defs
from roguelike_game.factories.monster import cache as monster_cache
from roguelike_editors.entities.entities_properties_panel.services.ecs_update_service import (
    update_player_assets,
    update_monster_assets,
    update_player_stats,
    update_monster_stats,
)
from roguelike_editors.entities.entities_properties_panel.services.entity_properties_service import (
    load_entity_data,
    save_entity_data,
    convert_value,
)

import logging
logger = logging.getLogger(__name__)



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
        # Controller de subtabs de asset set / no-set
        self.assets_subtabs_controller = EntitiesAssetsSubTabsController(self.model, font)
        self.view.assets_subtabs_controller = self.assets_subtabs_controller
        self.grid_controller.view.assets_subtabs_controller = self.assets_subtabs_controller
        # Assets picker panel
        self.assets_picker_controller = EntitiesAssetsPickerPanelController()
        self.view.assets_picker_controller = self.assets_picker_controller
        # Track last main tab to initialize asset sub-tabs
        self._last_active_type_tab = self.type_assets_controller.model.active_type_tab

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
        # Initialize asset sub-tab on first opening of assets
        current_type = self.type_assets_controller.model.active_type_tab
        if current_type == TYPE_TAB_ASSETS and self._last_active_type_tab != TYPE_TAB_ASSETS:
            entity_data = self.view._get_entity_data(self.model)
            active_set = entity_data.get('active_set', 'sets')
            desired = SUBTAB_SET if active_set == 'sets' else SUBTAB_NO_SET
            self.assets_subtabs_controller.model.active_sub_tab = desired
        # Update last active_type_tab
        self._last_active_type_tab = current_type
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
        logger.debug(f" _on_asset_chosen called with cell_key={cell_key}, path={path}")
        """
        Callback when asset is chosen: update entity property and persist to JSON.
        """
        ent_id = self.model.selected_id
        if not ent_id:
            return
        # update JSON and model
        json_path, data, entry = load_entity_data(ent_id, self.model.player_stats, self.model.monsters)
        # determine state and direction from cell_key
        # compute relative asset path
        abs_path = Path(path).resolve()
        assets_root = Path(ASSETS_DIR).resolve()
        try:
            rel = abs_path.relative_to(assets_root)
            rel_path = f"assets/{rel.as_posix()}"
        except ValueError:
            rel_path = str(path).replace("\\", "/")
        logger.debug(f" Computed rel_path={rel_path} for cell_key={cell_key}")

        parts = cell_key.split("_")
        # only asset grid updates supported
        if len(parts) == 3 and parts[0] == 'asset':
            _, state, direction = parts
            sub_tab = self.assets_subtabs_controller.model.active_sub_tab
            if ent_id in self.model.player_stats:
                assets_entry = entry.setdefault("assets", {})
                no_sets = assets_entry.setdefault("no-sets", {})
                sets = assets_entry.setdefault("sets", {})
                sprites_set = sets.setdefault("sprites_set", {})
                if sub_tab == SUBTAB_SET:
                    # update sprite sheet for this state for player
                    sprites_set[state] = [rel_path]
                else:
                    # update individual direction in no-sets for player
                    state_no_set = no_sets.setdefault(state, {})
                    state_no_set[direction] = rel_path
                # remove old erroneous sprites node for player
                entry.pop("sprites", None)
            elif ent_id in self.model.monsters:
                assets_entry = entry.setdefault("assets", {})
                no_sets = assets_entry.setdefault("no-sets", {})
                sets = assets_entry.setdefault("sets", {})
                sprites_set = sets.setdefault("sprites_set", {})
                if sub_tab == SUBTAB_SET:
                    # update sprite sheet for this state for monster
                    sprites_set[state] = [rel_path]
                else:
                    # update individual direction in no-sets for monster
                    state_no_set = no_sets.setdefault(state, {})
                    state_no_set[direction] = rel_path
                # remove old erroneous sprites node for monster
                entry.pop("sprites", None)
            # persist changes
            save_entity_data(ent_id, entry, json_path, self.model.player_stats, self.model.monsters)
        else:
            logging.error(f"[ERROR][PropertiesPanel] Invalid asset key for update: {cell_key}")
            return
        logger.debug(f" JSON saved for ent_id={ent_id}, cell_key={cell_key}")
        logger.debug(f" Saving entry and updating in-memory model for ent_id={ent_id}")
        # Update in-memory player_assets and reload config
        if ent_id in self.model.player_stats:
            self.model.player_assets[ent_id] = entry.get("assets", {})
            try:
                importlib.reload(pc)
            except Exception:
                pass
            # Update existing ECS player entities via service
            try:
                ecs_world = self.editor_controller.game.ecs.ecs_world
                update_player_assets(ecs_world, ent_id)
            except Exception as e:
                logging.error(f"[ERROR][PropertiesPanel] Error updating player ECS entities for class {ent_id}: {e}")
        logger.debug(f" Hiding assets picker panel")
        # Hide picker panel on success
        self.assets_picker_controller.hide()
        # Reset grid animators to force reload
        logger.debug(f" Resetting grid controller cache (last_entity_id and last_state_tab)")
        self.grid_controller.model.last_entity_id = None
        self.grid_controller.model.last_state_tab = None
        # Force immediate redraw of properties panel to reflect new asset
        try:
            self.editor_controller.render(self.editor_controller.game.screen)
        except Exception:
            pass
        # Refresh monster asset caches and update ECS entities immediately (only for monsters)
        if ent_id not in self.model.player_stats:
            try:
                ecs_world = self.editor_controller.game.ecs.ecs_world
                update_monster_assets(ecs_world, ent_id)
            except Exception as e:
                logging.error(f"[ERROR][PropertiesPanel] Error updating monster assets for ent_id={ent_id}: {e}")
    # ----------------------------
    # COMMIT DE CAMBIOS
    # ----------------------------
    def _on_active_set_toggled(self, ent_id: str) -> None:
        """
        Maneja el cambio de active_set para jugadores y monstruos: recarga/actualiza grid y ECS.
        """
        logger.debug(f" Active set toggled for ent_id={ent_id}")
        # Reset grid cache to force rebuild on next draw
        self.grid_controller.model.last_entity_id = None
        self.grid_controller.model.last_state_tab = None
        if ent_id in self.model.player_stats:
            # Players: recargar config y actualizar ECS de jugadores
            try:
                importlib.reload(pc)
            except Exception:
                pass
            try:
                ecs_world = self.editor_controller.game.ecs.ecs_world
                update_player_assets(ecs_world, ent_id)
                logger.debug(f" Player ECS entities updated for class {ent_id} after active_set toggle")
            except Exception as e:
                logging.error(f"[ERROR][PropertiesPanel] Error updating player ECS entities on active_set toggle for class {ent_id}: {e}")
        else:
            # Monsters: recargar definiciones/cachés y actualizar ECS de monstruos
            try:
                ecs_world = self.editor_controller.game.ecs.ecs_world
                update_monster_assets(ecs_world, ent_id)
                logger.debug(f" Monster ECS entities updated for type {ent_id} after active_set toggle")
            except Exception as e:
                logging.error(f"[ERROR][PropertiesPanel] Error updating monster ECS entities on active_set toggle for type {ent_id}: {e}")
        # Redraw properties panel UI
        try:
            self.editor_controller.render(self.editor_controller.game.screen)
        except Exception:
            pass

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
        path, data, entry = load_entity_data(ent_id, self.model.player_stats, self.model.monsters)

        # Conversión del valor editado al tipo original y actualización adecuada
        if ent_id in self.model.player_stats:
            # Propiedad genérica para jugadores
            old_val = entry.get(key)
            converted = convert_value(new_text, old_val)
            entry[key] = converted
        elif ent_id in self.model.monsters:
            # Estadística anidada para monstruos
            stats = entry.setdefault('stats', {})
            old_val = stats.get(key)
            converted = convert_value(new_text, old_val)
            stats[key] = converted
        else:
            # Fallback genérico
            old_val = entry.get(key)
            converted = convert_value(new_text, old_val)
            entry[key] = converted
        # Persistir cambios en JSON
        save_entity_data(ent_id, entry, path, self.model.player_stats, self.model.monsters)
        # Reload monster definitions and clear sprite caches for updated stats
        if ent_id in self.model.monsters:
            reload_monster_defs()
            monster_cache._loaded_variants.discard(ent_id)
            monster_cache._SPRITE_SURFACES.pop(ent_id, None)
            monster_cache._DEATH_SURFACES.pop(ent_id, None)

        # Reset de estado de edición
        self._reset_edit_state()

        # Propagate stat changes to in-memory model and ECS world
        if ent_id in self.model.player_stats:
            # Update in-memory model
            self.model.player_stats[ent_id][key] = converted
            try:
                ecs_world = self.editor_controller.game.ecs.ecs_world
                update_player_stats(ecs_world, ent_id, key, converted)
            except Exception as e:
                logging.error(f'[ERROR][PropertiesPanel] Error updating player ECS stats for class {ent_id}: {e}')
        elif ent_id in self.model.monsters:
            # Update in-memory model
            self.model.monsters.setdefault(ent_id, {}).setdefault('stats', {})[key] = converted
            try:
                ecs_world = self.editor_controller.game.ecs.ecs_world
                update_monster_stats(ecs_world, ent_id, key, converted)
            except Exception as e:
                logging.error(f'[ERROR][PropertiesPanel] Error updating monster ECS stats for type {ent_id}: {e}')

    # ----------------------------
    # UTILIDADES PRIVADAS
    # ----------------------------
    def _load_entity_data(self, ent_id: str) -> tuple[str, dict, dict]:
        """
        Carga los datos JSON del jugador o monstruo correspondiente.

        Returns:
            (path, data, entry): Ruta al archivo, diccionario raíz, y la entrada de la entidad.
        """
        # Delegar al servicio centralizado
        return load_entity_data(ent_id, self.model.player_stats, self.model.monsters)

    def _save_entity_data(self, ent_id: str, entry: dict, path: str, data: dict) -> None:
        """
        Persiste los cambios en el archivo JSON correspondiente y actualiza el modelo.
        """
        # Delegar al servicio centralizado
        save_entity_data(ent_id, entry, path, self.model.player_stats, self.model.monsters)
        if ent_id not in self.model.player_stats:
            # Mantener sincronizado el caché de modelo de monstruos
            self.model.monsters[ent_id] = entry

    def _convert_value(self, new_text: str, old_val: any) -> any:
        """
        Convierte el valor ingresado al tipo original (bool, int, float, str).
        Si falla la conversión, se mantiene como string.
        """
        return convert_value(new_text, old_val)

    def _reset_edit_state(self) -> None:
        """Limpia las variables relacionadas con la edición."""
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
        self.model.focused_property = None
