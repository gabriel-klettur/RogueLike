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
from roguelike_editors.entities.entities_properties_panel.services.entity_flatten import (
    flatten_entity_data,
)
from roguelike_editors.entities.services.commands import (
    EditPropertyCommand,
    SetAssetCommand,
    RenameEntityCommand,
)
from roguelike_editors.entities.services.constants import ADD_ENTITIES_ON_SYSTEM

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
            ent_id = self.model.hovered_entity_id or self.model.selected_id
            flattened = flatten_entity_data(self.model.player_stats, self.model.player_assets, self.model.monsters, ent_id)
            active_set = flattened.get('active_set', 'sets')
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
        Callback when asset is chosen: push undoable SetAssetCommand.
        """
        ent_id = self.model.selected_id
        if not ent_id:
            return
        # Si estamos en modo 'Add Entities on System', no persistir aún: actualizar solo en memoria
        in_add_system_mode = False
        try:
            in_add_system_mode = (self.editor_controller.model.add_remove_model.active_tool == ADD_ENTITIES_ON_SYSTEM)
        except Exception:
            in_add_system_mode = False
        if in_add_system_mode:
            # Actualización en memoria para assets
            # Normalizamos ruta relativa como hace SetAssetCommand en persistencia; aquí podemos almacenar la ruta tal cual
            # Estructura destino: entry['assets'] con active_set 'no-sets' por defecto en monstruos nuevos
            # Convertir cualquier PathLike a str y normalizar separadores para evitar problemas de serialización
            try:
                path_str = os.fspath(path)
            except Exception:
                path_str = path
            if isinstance(path_str, str):
                path_str = path_str.replace('\\\\', '/').replace('\\', '/')
            if ent_id in self.model.player_stats:
                # Players: mantener espejo en player_assets
                assets = self.model.player_assets.setdefault(ent_id, {})
                default_active = assets.get('active_set', 'sets')
                active = assets.get('active_set', default_active)
            else:
                m_entry = self.model.monsters.setdefault(ent_id, {})
                # Marcar como pendiente para que el picker lo oculte hasta confirmar
                if isinstance(m_entry, dict):
                    m_entry['__pending__'] = True
                assets = m_entry.setdefault('assets', {})
                default_active = 'no-sets'
                active = assets.get('active_set', default_active)
            parts = cell_key.split('_')
            if len(parts) == 3 and parts[0] == 'asset':
                _, state, direction = parts
                if active == 'sets':
                    sprites_set = assets.setdefault('sets', {}).setdefault('sprites_set', {})
                    sprites_set[state] = [path_str]
                else:
                    no_sets = assets.setdefault('no-sets', {})
                    no_sets.setdefault(state, {})[direction] = path_str
                # limpiar legado si existiera
                assets.pop('sprites', None)
            # UI refresh ligera
            self.assets_picker_controller.hide()
            self.grid_controller.model.last_entity_id = None
            self.grid_controller.model.last_state_tab = None
            try:
                self.editor_controller.render(self.editor_controller.game.screen)
            except Exception:
                pass
            return
        # Fuera de add-mode: Push command into editor history
        self.editor_controller.history.push(SetAssetCommand(self, ent_id, cell_key, path))
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
        Aplica los cambios editados en la propiedad seleccionada.
        En modo 'Add Entities on System' solo actualiza en memoria; fuera de ese modo, persiste vía comandos.
        """
        if not self.model.editing_property or not self.model.selected_id:
            return

        ent_id = self.model.selected_id
        key = self.model.editing_property
        new_text = self.model.editing_text
        # Detectar modo Add-Entities-On-System
        in_add_system_mode = False
        try:
            in_add_system_mode = (self.editor_controller.model.add_remove_model.active_tool == ADD_ENTITIES_ON_SYSTEM)
        except Exception:
            in_add_system_mode = False

        if in_add_system_mode:
            # Actualizar solo en memoria (ramifica por tipo Player/Monster)
            is_selector = getattr(self.model, 'show_add_system_selector', False)
            sel_type = getattr(self.model, 'add_system_entity_type', 'Monster')
            target_is_player = (ent_id in self.model.player_stats) or (is_selector and sel_type == 'Player')

            if key == 'id':
                new_id = new_text.strip() or ent_id
                if target_is_player:
                    if new_id and new_id != ent_id:
                        # Mover entrada en player_stats y reflejar assets si existen
                        p_stats = self.model.player_stats.pop(ent_id, None)
                        if p_stats is None:
                            p_stats = {}
                        self.model.player_stats[new_id] = p_stats
                        # Renombrar assets espejo si existen
                        if isinstance(self.model.player_assets, dict) and ent_id in self.model.player_assets:
                            self.model.player_assets[new_id] = self.model.player_assets.pop(ent_id)
                    # Actualizar selección y salir
                    self.model.selected_id = new_id
                    self._reset_edit_state()
                    return
                else:
                    # Monstruo temporal
                    if new_id and new_id != ent_id:
                        entry = self.model.monsters.pop(ent_id, None)
                        if entry is None:
                            entry = {'stats': {}, 'assets': {'active_set': 'no-sets', 'sets': {}, 'no-sets': {}}}
                    else:
                        entry = self.model.monsters.get(ent_id)
                        if entry is None:
                            entry = {'stats': {}, 'assets': {'active_set': 'no-sets', 'sets': {}, 'no-sets': {}}}
                    # Marcar como pendiente hasta confirmar
                    if isinstance(entry, dict):
                        entry['__pending__'] = True
                    self.model.monsters[new_id] = entry
                    self.model.selected_id = new_id
                    self._reset_edit_state()
                    return

            # Otras propiedades: escribir en stats de Player o Monster (soporta claves con puntos)
            if target_is_player:
                stats = self.model.player_stats.setdefault(ent_id, {})
                # Inferir tipo desde valor actual si existe
                old_val = None
                if '.' in key:
                    parts = key.split('.')
                    cur = stats
                    for i, p in enumerate(parts):
                        if not isinstance(cur, dict):
                            cur = None
                            break
                        if i == len(parts) - 1:
                            old_val = cur.get(p)
                        else:
                            cur = cur.get(p, {})
                else:
                    old_val = stats.get(key)
                new_val = convert_value(new_text, old_val)
                # Set nested value
                if '.' in key:
                    parts = key.split('.')
                    cur = stats
                    for i, p in enumerate(parts):
                        if i == len(parts) - 1:
                            cur[p] = new_val
                        else:
                            nxt = cur.get(p)
                            if not isinstance(nxt, dict):
                                nxt = {}
                                cur[p] = nxt
                            cur = nxt
                else:
                    stats[key] = new_val
                self._reset_edit_state()
                return
            else:
                m_entry = self.model.monsters.setdefault(ent_id, {})
                # Marcar como pendiente mientras esté en modo add-system
                if isinstance(m_entry, dict):
                    m_entry['__pending__'] = True
                stats = m_entry.setdefault('stats', {})
                # Inferir tipo desde valor actual si existe
                old_val = None
                # Navegar nested para obtener old_val si aplica
                if '.' in key:
                    parts = key.split('.')
                    cur = stats
                    for i, p in enumerate(parts):
                        if not isinstance(cur, dict):
                            cur = None
                            break
                        if i == len(parts) - 1:
                            old_val = cur.get(p)
                        else:
                            cur = cur.get(p, {})
                else:
                    old_val = stats.get(key)
                new_val = convert_value(new_text, old_val)
                # Set nested value
                if '.' in key:
                    parts = key.split('.')
                    cur = stats
                    for i, p in enumerate(parts):
                        if i == len(parts) - 1:
                            cur[p] = new_val
                        else:
                            nxt = cur.get(p)
                            if not isinstance(nxt, dict):
                                nxt = {}
                                cur[p] = nxt
                            cur = nxt
                else:
                    stats[key] = new_val
                # No persistir; solo limpiar estado de edición
                self._reset_edit_state()
                return
        # Special-case: renaming the entity id (players or monsters)
        if key == 'id':
            new_id = new_text.strip()
            if new_id and new_id != ent_id:
                self.editor_controller.history.push(RenameEntityCommand(self, ent_id, new_id))
            # Clear edit state regardless
            self._reset_edit_state()
            return
        # Default: push undoable property edit command
        self.editor_controller.history.push(EditPropertyCommand(self, ent_id, key, new_text))

    # ----------------------------
    # CONFIRMAR AÑADIR ENTIDAD AL SISTEMA
    # ----------------------------
    def confirm_add_entity_on_system(self) -> None:
        """Persiste la entidad actualmente seleccionada y sale del modo de añadir en sistema."""
        sel_id = getattr(self.model, 'selected_id', None)
        if not sel_id:
            return
        try:
            # Determinar tipo por pertenencia
            is_player = sel_id in self.model.player_stats
            if is_player:
                # Componer entrada de jugador desde estados en memoria (stats + assets)
                p_stats = self.model.player_stats.get(sel_id, {})
                p_assets = self.model.player_assets.get(sel_id, {}) if isinstance(self.model.player_assets, dict) else {}
                entry = {
                    'stats': p_stats,
                    'assets': p_assets,
                }
                path, _, _ = load_entity_data(sel_id, self.model.player_stats, self.model.monsters)
                save_entity_data(sel_id, entry, path, self.model.player_stats, self.model.monsters)
                logger.debug(f"Player class '{sel_id}' confirmed and saved to JSON")
                # Limpiar posible entrada temporal de monstruo con el mismo id
                try:
                    temp = self.model.monsters.get(sel_id)
                    if isinstance(temp, dict) and temp.get('__pending__'):
                        self.model.monsters.pop(sel_id, None)
                except Exception:
                    pass
            else:
                path, data, entry = load_entity_data(sel_id, self.model.player_stats, self.model.monsters)
                # Mezclar entrada temporal en memoria si existe (monstruo)
                cur = self.model.monsters.get(sel_id)
                if cur is not None:
                    entry.update(cur)
                    # Eliminar flag de pendiente antes de guardar
                    if isinstance(entry, dict):
                        entry.pop('__pending__', None)
                    if isinstance(cur, dict):
                        cur.pop('__pending__', None)
                save_entity_data(sel_id, entry, path, self.model.player_stats, self.model.monsters)
                logger.debug(f"Monster type '{sel_id}' confirmed and saved to JSON")
                # Recargar definiciones de monstruos para habilitar spawn inmediato
                try:
                    reload_monster_defs()
                    logger.debug("Definiciones de monstruos recargadas tras confirmar")
                except Exception as e:
                    logger.error(f"[WARN][PropertiesPanel] No se pudieron recargar definiciones de monstruos: {e}")
        except Exception as e:
            logger.error(f"[ERROR][PropertiesPanel] Error al confirmar entidad '{sel_id}': {e}")
        # Salir del modo 'add_entities_on_system' y ocultar selector/botón en UI
        try:
            arm = self.editor_controller.model.add_remove_model
            if getattr(arm, 'active_tool', None) == ADD_ENTITIES_ON_SYSTEM:
                arm.active_tool = None
            # Ocultar controles del selector
            self.model.show_add_system_selector = False
            self.model.entity_type_rect = None
            if hasattr(self.model, 'confirm_button_rect'):
                self.model.confirm_button_rect = None
        except Exception:
            pass
        # Redibujar UI
        try:
            self.editor_controller.render(self.editor_controller.game.screen)
        except Exception:
            pass

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
