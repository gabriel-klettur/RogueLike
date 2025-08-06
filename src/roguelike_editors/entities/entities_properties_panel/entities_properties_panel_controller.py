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
from roguelike_editors.entities.entities_properties_panel.entities_set_ot_assets_tab.entities_set_ot_assets_tab_controller import EntitiesSetOtAssetsTabController
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_controller import EntitiesAssetsPickerPanelController
import importlib
import roguelike_game.config.players_config as pc
from roguelike_game.factories.player.loader import load_and_scale_sprites, extract_initial_frame, build_animator_map
from roguelike_game.factories.monster.config import reload_monster_defs
from roguelike_game.factories.monster import cache as monster_cache


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
        self.set_ot_assets_tab_controller = EntitiesSetOtAssetsTabController(self.model, font)
        self.view.set_ot_assets_tab_controller = self.set_ot_assets_tab_controller
        self.grid_controller.view.set_ot_assets_tab_controller = self.set_ot_assets_tab_controller
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
        logging.debug(f" _on_asset_chosen called with cell_key={cell_key}, path={path}")
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
        logging.debug(f" Computed rel_path={rel_path} for cell_key={cell_key}")

        parts = cell_key.split("_")
        # only asset grid updates supported
        if len(parts) == 3 and parts[0] == 'asset':
            _, state, direction = parts
            sub_tab = self.set_ot_assets_tab_controller.model.active_sub_tab
            if ent_id in self.model.player_stats:
                assets_entry = entry.setdefault("assets", {})
                no_sets = assets_entry.setdefault("no-sets", {})
                sets = assets_entry.setdefault("sets", {})
                sprites_set = sets.setdefault("sprites_set", {})
                if sub_tab == 'asset set':
                    # update sprite sheet for this state for player
                    sprites_set[state] = [rel_path]
                else:
                    # update individual direction in no-sets for player
                    state_no_set = no_sets.setdefault(state, {})
                    state_no_set[direction] = rel_path
                # remove old erroneous sprites node for player
                entry.pop("sprites", None)
            elif ent_id in self.model.monsters:
                sprites = entry.setdefault("sprites", {})
                nested_assets = sprites.setdefault("assets", {})
                if sub_tab == 'asset set':
                    # update all directions for monster using sheet path
                    dirs = nested_assets.setdefault(state, {})
                    for dkey in dirs:
                        dirs[dkey] = rel_path
                else:
                    state_dict = nested_assets.setdefault(state, {})
                    state_dict[direction] = rel_path
            # persist changes
            self._save_entity_data(ent_id, entry, json_path, data)
        else:
            logging.error(f"[ERROR][PropertiesPanel] Invalid asset key for update: {cell_key}")
            return
        logging.debug(f" JSON saved for ent_id={ent_id}, cell_key={cell_key}")
        logging.debug(f" Saving entry and updating in-memory model for ent_id={ent_id}")
        # Update in-memory player_assets and reload config
        if ent_id in self.model.player_stats:
            self.model.player_assets[ent_id] = entry.get("assets", {})
            try:                
                importlib.reload(pc)
            except Exception:
                pass
            # Update existing ECS player entities
            try:                
                ecs_world = self.editor_controller.game.ecs.ecs_world
                player_tags = ecs_world.components.get('PlayerTagComponent', {})
                sprites_comp = ecs_world.components.get('Sprite', {})
                animators = ecs_world.components.get('Animator', {})
                sprites_dict = load_and_scale_sprites(ent_id)
                initial_frame = extract_initial_frame(sprites_dict)
                anim_map = build_animator_map(sprites_dict)
                for eid, tag in player_tags.items():
                    if tag.class_name == ent_id:
                        if initial_frame and eid in sprites_comp:
                            img = initial_frame.copy() if hasattr(initial_frame, 'copy') else initial_frame
                            sprites_comp[eid].image = img
                        if eid in animators:
                            animators[eid].animations = anim_map
                logging.debug(f" Player ECS entities updated for class {ent_id}")
            except Exception as e:
                logging.error(f"[ERROR][PropertiesPanel] Error updating player ECS entities for class {ent_id}: {e}")
        logging.debug(f" Hiding assets picker panel")
        # Hide picker panel on success
        self.assets_picker_controller.hide()
        # Reset grid animators to force reload
        logging.debug(f" Resetting grid controller cache (last_entity_id and last_state_tab)")
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
                # Reload definitions and clear caches for this type
                reload_monster_defs()
                monster_cache._loaded_variants.discard(ent_id)
                monster_cache._SPRITE_SURFACES.pop(ent_id, None)
                monster_cache._DEATH_SURFACES.pop(ent_id, None)
                monster_cache.load_caches_for([ent_id])
                logging.debug(f" Monster defs reloaded and cache cleared for ent_id={ent_id}")
            except Exception as e:
                logging.error(f"[ERROR][PropertiesPanel] Error reloading monster caches for ent_id={ent_id}: {e}")
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
    # ----------------------------
    # COMMIT DE CAMBIOS
    # ----------------------------
    def _on_active_set_toggled(self, ent_id: str) -> None:
        """
        Maneja el cambio de active_set para jugadores: recarga configuración, actualiza grid y ECS.
        """
        logging.debug(f" Active set toggled for ent_id={ent_id}")
        try:
            importlib.reload(pc)
        except Exception:
            pass
        # Reset grid cache to force rebuild on next draw
        self.grid_controller.model.last_entity_id = None
        self.grid_controller.model.last_state_tab = None
        # Update ECS player entities with new assets
        try:
            ecs_world = self.editor_controller.game.ecs.ecs_world
            player_tags = ecs_world.components.get('PlayerTagComponent', {})
            sprites_comp = ecs_world.components.get('Sprite', {})
            animators = ecs_world.components.get('Animator', {})
            sprites_dict = load_and_scale_sprites(ent_id)
            initial_frame = extract_initial_frame(sprites_dict)
            anim_map = build_animator_map(sprites_dict)
            for eid, tag in player_tags.items():
                if tag.class_name == ent_id:
                    if initial_frame and eid in sprites_comp:
                        img = initial_frame.copy() if hasattr(initial_frame, 'copy') else initial_frame
                        sprites_comp[eid].image = img
                    if eid in animators:
                        animators[eid].animations = anim_map
            logging.debug(f" Player ECS entities updated for class {ent_id} after active_set toggle")
        except Exception as e:
            logging.error(f"[ERROR][PropertiesPanel] Error updating player ECS entities on active_set toggle for class {ent_id}: {e}")
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
            # Ruta al nuevo JSON de jugadores
            path = os.path.join(os.getcwd(), "data", "entities", "new_players.json")
            root = load_from_json(path)
            # Extraer clases y datos de jugadores
            classes = root.get("players", {}).get("classes", {})
            data = classes
        else:
            path = os.path.join(os.getcwd(), "data", "entities", "new_monsters.json")
            data = load_from_json(path)

        entry = data.get(ent_id, {})
        return path, data, entry

    def _save_entity_data(self, ent_id: str, entry: dict, path: str, data: dict) -> None:
        """
        Persiste los cambios en el archivo JSON correspondiente y actualiza el modelo.
        """
        if ent_id in self.model.player_stats:
            # Guardar en JSON anidado de jugadores
            full = path
            root = load_from_json(full)
            root.setdefault("players", {}).setdefault("classes", {})[ent_id] = entry
            with open(full, "w", encoding="utf-8") as f:
                json.dump(root, f, ensure_ascii=False, indent=2)

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
