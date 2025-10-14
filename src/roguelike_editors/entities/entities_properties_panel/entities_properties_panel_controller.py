import pygame
import logging

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
from roguelike_editors.entities.entities_properties_panel.services.entity_flatten import (
    flatten_entity_data,
)
# Services (delegation)
from roguelike_editors.entities.entities_properties_panel.services.asset_choice_service import (
    handle_asset_chosen,
)
from roguelike_editors.entities.entities_properties_panel.services.active_set_service import (
    handle_active_set_toggled,
)
from roguelike_editors.entities.entities_properties_panel.services.edit_commit_service import (
    commit_edit,
)
from roguelike_editors.entities.entities_properties_panel.services.add_entity_service import (
    confirm_add_entity_on_system as confirm_add_entity_service,
)

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
        Callback when asset is chosen: delegate to asset choice service.
        """
        handle_asset_chosen(self, cell_key, path)
    # ----------------------------
    # COMMIT DE CAMBIOS
    # ----------------------------
    def _on_active_set_toggled(self, ent_id: str) -> None:
        """
        Maneja el cambio de active_set delegando en el servicio correspondiente.
        """
        logger.debug(f" Active set toggled for ent_id={ent_id}")
        handle_active_set_toggled(self, ent_id)

    def _commit_edit(self) -> None:
        """
        Aplica los cambios editados delegando en el servicio de commit de edición.
        """
        commit_edit(self)

    # ----------------------------
    # CONFIRMAR AÑADIR ENTIDAD AL SISTEMA
    # ----------------------------
    def confirm_add_entity_on_system(self) -> None:
        """Persiste la entidad y sale del modo de añadir, delegando en el servicio."""
        confirm_add_entity_service(self)

    def _reset_edit_state(self) -> None:
        """Limpia las variables relacionadas con la edición."""
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
        self.model.focused_property = None
