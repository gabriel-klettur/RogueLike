import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE

import logging
logger = logging.getLogger(__name__)
from roguelike_editors.entities.services.ui_helpers import hide_assets_picker_and_clear_properties
from roguelike_editors.entities.services.camera_helpers import screen_to_tile
from roguelike_editors.entities.services.entity_lookup import find_clickable_entity_at
from roguelike_editors.entities.services.spawn_services import spawn_entity
from roguelike_editors.entities.services.constants import ENTITIES_TOOLS, UI_MARGIN, ADD_ENTITIES_ON_SYSTEM
from roguelike_editors.entities.services.history import HistoryManager
from roguelike_editors.entities.services.commands import (
    SpawnEntityCommand,
    DeleteEntityCommand,
)

from roguelike_editors.entities.entities_editor_model import EntitiesEditorModel
from roguelike_editors.entities.entities_title.entities_title_controller import EntitiesTitleController
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_controller import EntitiesToolBarPanelController
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_view import EntitiesToolBarPanelView
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_events import EntitiesToolBarPanelEventHandler
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_controller import EntitiesAddRemovePanelController
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_view import EntitiesAddRemovePanelView
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_events import EntitiesAddRemovePanelEventHandler
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_controller import EntityPickerPanelController
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller import EntityPropertiesPanelController

class EntitiesEditorController:
    """
    Controlador principal del editor de entidades en arquitectura MVC.
    Orquesta modelos y subcontrollers (title, toolbar, add/remove, picker, properties).
    """
    def __init__(self, model: EntitiesEditorModel, font: pygame.font.Font):
        self.model = model
        self.font = font
        # History manager for undo/redo
        self.history = HistoryManager()
        # Título
        self.title_controller = EntitiesTitleController(self, self.model.title_model, self.font)
        # Toolbar
        self.toolbar_event_handler = EntitiesToolBarPanelEventHandler(self, self.model.toolbar_model)
        self.toolbar_view = EntitiesToolBarPanelView(self, self.model.toolbar_model)
        self.toolbar_controller = EntitiesToolBarPanelController(
            self, self.model.toolbar_model, self.toolbar_view, self.toolbar_event_handler
        )
        # Add/Remove
        self.add_remove_event_handler = EntitiesAddRemovePanelEventHandler(self, self.model.add_remove_model)
        self.add_remove_view = EntitiesAddRemovePanelView(self, self.model.add_remove_model)
        self.add_remove_controller = EntitiesAddRemovePanelController(
            self, self.model.add_remove_model, self.add_remove_view, self.add_remove_event_handler
        )
        # Picker
        self.picker_controller = EntityPickerPanelController(
            self.model.player_stats, self.model.monsters, self.model.assets, self.font
        )
        # Inicializar posición del picker panel a la derecha del add/remove panel
        margin = UI_MARGIN
        add_rem_widget = self.add_remove_view.widget
        add_pos = add_rem_widget.panel.pos or (add_rem_widget.x, add_rem_widget.y)
        add_w, _ = add_rem_widget.panel.surface.get_size()
        self.picker_controller.view.x = add_pos[0] + add_w + margin
        self.picker_controller.view.y = add_pos[1]
        # Properties
        self.properties_controller = EntityPropertiesPanelController(
            self, self.model.player_stats, self.model.monsters, self.model.player_assets, self.font
        )
        # Vista (separa render)
        from roguelike_editors.entities.entities_editor_view import EntitiesEditorView
        self.view = EntitiesEditorView(self)

    def open_new_monster_properties(self) -> None:
        """
        Create a new blank monster class entry in-memory and open the Properties Panel
        for editing its fields (including assigning a new id).
        """
        # Ensure we are not in spawn/delete modes
        if self.model.spawn_mode_active:
            self.exit_spawn_mode()
        if self.model.delete_mode_active:
            self.exit_delete_mode()

        # Generate a unique temporary id
        base = 'new_monster'
        new_id = base
        idx = 2
        while new_id in self.model.monsters or new_id in self.model.player_stats:
            new_id = f"{base}_{idx}"
            idx += 1

        # Prepare a blank monster template (stats empty, assets None/defaults)
        directions = ['s', 'se', 'e', 'ne', 'n', 'nw', 'w', 'sw']
        states = ['idle', 'walk', 'chase', 'cast', 'attack', 'damage', 'death']
        def empty_dirs():
            return {d: None for d in directions}
        no_sets = {st: empty_dirs() for st in states}
        no_sets['sprites_data_no-set'] = {
            'scale_idle': None,
            'scale_walk': None,
            'scale_chase': None,
            'scale_cast': None,
            'scale_attack': None,
            'scale_damage': None,
            'scale_death': None,
            'tint': None,
        }
        sets = {
            'sprites_set': {st: [] for st in states},
            'sprites_data_set': {
                'scale_idle': None,
                'scale_walk': None,
                'scale_chase': None,
                'scale_cast': None,
                'scale_attack': None,
                'scale_damage': None,
                'scale_death': None,
                'tint': None,
            }
        }
        self.model.monsters[new_id] = {
            'stats': {},
            'assets': {
                'active_set': 'no-sets',
                'sets': sets,
                'no-sets': no_sets,
            }
        }

        # Select the new entity in the properties panel and make it visible
        self.properties_controller.model.hovered_entity_id = None
        self.properties_controller.model.selected_id = new_id
        # Ensure picker is visible and not blinking (not in spawn)
        self.picker_controller.model.visible = True
        self.picker_controller.model.blink = False
        # Redraw to reflect the panel opening
        try:
            self.render(self.game.screen)
        except Exception:
            pass

    def enter_spawn_mode(self, entity_type=None):
        """
        Inicia modo spawn de entidades: picker parpadeante y selección inicial.
        """
        # Cancelar delete mode si está activo
        if self.model.delete_mode_active:
            self.exit_delete_mode()
        self.model.spawn_mode_active = True
        self.model.spawn_entity_type = entity_type
        # Iniciar parpadeo en picker
        self.picker_controller.model.blink = True
        # Mostrar picker
        self.picker_controller.model.visible = True
        # Reset selección previa
        self.picker_controller.model.selected_id = None
        # Cerrar Assets Picker y limpiar estado del panel de propiedades durante spawn
        hide_assets_picker_and_clear_properties(self.properties_controller)

    def exit_spawn_mode(self):
        """
        Sale de modo spawn de entidades.
        """
        self.model.spawn_mode_active = False
        self.model.spawn_entity_type = None
        # Detener parpadeo
        self.picker_controller.model.blink = False
        # Detener parpadeo de selección
        self.picker_controller.model.selection_blink = False
        # Restablecer cursor
        pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)

    def enter_delete_mode(self):
        """
        Entra en modo borrar entidades.
        """
        # Cancelar spawn mode si está activo
        if self.model.spawn_mode_active:
            self.exit_spawn_mode()
        self.model.delete_mode_active = True
        # Cambiar cursor a cruz
        pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_CROSSHAIR)
        # Cerrar Assets Picker y limpiar estado del panel de propiedades durante delete
        hide_assets_picker_and_clear_properties(self.properties_controller)

    def exit_delete_mode(self):
        """
        Sale de modo borrar entidades.
        """
        self.model.delete_mode_active = False
        # Restaurar cursor flecha
        pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)

    def is_active(self, tool: str) -> bool:
        """Retorna True si la herramienta está activa en el toolbar."""
        return self.model.toolbar_model.active_tool == tool

    def handle_event(self, event: pygame.event.Event) -> bool:
        # Debug global entities_on_map: click event recibido
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            logger.debug(f" Click global en {event.pos}, spawn_mode={self.model.spawn_mode_active}, spawn_entity_type={self.model.spawn_entity_type}")

        """
        Delega el evento a los subcontrollers en orden de prioridad.
        Retorna True si fue consumido.
        """
        # Global shortcuts: Undo/Redo
        if event.type == pygame.KEYDOWN:
            mods = pygame.key.get_mods()
            if mods & pygame.KMOD_CTRL and event.key == pygame.K_z:
                if self.history.undo():
                    logger.debug(" Undo executed")
                return True
            if mods & pygame.KMOD_CTRL and (event.key == pygame.K_y or (mods & pygame.KMOD_SHIFT and event.key == pygame.K_z)):
                if self.history.redo():
                    logger.debug(" Redo executed")
                return True
        if self.title_controller.handle_event(event):
            return True
        if self.toolbar_controller.handle_event(event):
            return True
        active = self.model.toolbar_model.active_tool
        if active in ENTITIES_TOOLS:
            # Add/Remove panel
            if self.add_remove_controller.handle_event(event):
                return True
            # Picker panel
            self.picker_controller.handle_event(event)
            # Sincronizar hover y seleccionado para properties panel
            hovered = self.picker_controller.model.hovered_id
            selected = self.picker_controller.model.selected_id
            in_add_system_mode = (self.model.add_remove_model.active_tool == ADD_ENTITIES_ON_SYSTEM)
            if not (self.model.delete_mode_active or self.model.spawn_mode_active):
                if not in_add_system_mode:
                    # Modo normal: sincronizar con Picker
                    self.properties_controller.model.hovered_entity_id = hovered
                    self.properties_controller.model.selected_id = selected
                else:
                    # Modo "Add Entity on System": preservar selección del Properties Panel
                    # Evitar que el picker reemplace la selección del nuevo entity temporal
                    self.properties_controller.model.hovered_entity_id = None
            else:
                # Mantener cerrado el panel de propiedades durante delete/spawn
                self.properties_controller.model.hovered_entity_id = None
                self.properties_controller.model.selected_id = None
            # Selección de entidad tras click en picker en modo spawn
            if self.model.spawn_mode_active and self.model.spawn_entity_type is None and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                sel = self.picker_controller.model.selected_id
                if sel:
                    self.model.spawn_entity_type = sel
                    # Detener parpadeo y fijar borde
                    self.picker_controller.model.blink = False
                    # Iniciar parpadeo de selección
                    self.picker_controller.model.selection_blink = True
                    # Cambiar cursor a crosshair
                    pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_CROSSHAIR)
                    return True
            # Si el ratón está sobre el panel de picker, consumir el evento y no propagar al mapa
            if hasattr(event, 'pos') and self.picker_controller.model.visible:
                panel_rect = self.picker_controller.model.panel_rect
                if panel_rect and panel_rect.collidepoint(event.pos):
                    return True
            # Properties panel (solo interactivo si no estamos en delete/spawn)
            if not (self.model.delete_mode_active or self.model.spawn_mode_active):
                if self.properties_controller.handle_event(event):
                    return True
            # Delete entity on map in delete mode
            if self.model.delete_mode_active and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                mx, my = event.pos
                eid = find_clickable_entity_at(self.game, mx, my)
                if eid is not None:
                    # Push undoable delete command
                    self.history.push(DeleteEntityCommand(self, eid))
                    logger.debug(f" Entity {eid} delete command queued via click at ({mx},{my})")
                    self.exit_delete_mode()
                    return True
            # Completando spawn: click en mapa finaliza spawn_mode
            if self.model.spawn_mode_active and self.model.spawn_entity_type and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                etype = self.model.spawn_entity_type
                sx, sy = event.pos
                tx, ty = screen_to_tile(self.game.camera, sx, sy, TILE_SIZE)
                # Crear entidad en ECS mediante servicio
                self.history.push(SpawnEntityCommand(self, etype, tx, ty))
                logger.debug(f" Spawn command for '{etype}' at tile ({tx},{ty}) queued")
                self.exit_spawn_mode()
                return True
        return False

    def update(self, camera, game_map=None):
        """
        Actualiza la lógica de panning si es necesario.
        """
        # Implementar si el editor necesita actualizar algo continuo
        pass

    def render(self, screen: pygame.Surface) -> None:
        """
        Delegar render a la vista especializada.
        """
        self.view.render(screen)
