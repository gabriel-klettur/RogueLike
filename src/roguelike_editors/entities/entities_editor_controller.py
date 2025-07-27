import pygame
from roguelike_game.factories.registry import get_factory
from roguelike_engine.config.config_tiles import TILE_SIZE

from pathlib import Path
from roguelike_ui.services.json_persistence import load_from_json

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
        margin = 8
        add_rem_widget = self.add_remove_view.widget
        add_pos = add_rem_widget.panel.pos or (add_rem_widget.x, add_rem_widget.y)
        add_w, _ = add_rem_widget.panel.surface.get_size()
        self.picker_controller.view.x = add_pos[0] + add_w + margin
        self.picker_controller.view.y = add_pos[1]
        # Properties
        self.properties_controller = EntityPropertiesPanelController(
            self.model.player_stats, self.model.monsters, self.font
        )
        # Vista (separa render)
        from roguelike_editors.entities.entities_editor_view import EntitiesEditorView
        self.view = EntitiesEditorView(self)

    def enter_spawn_mode(self, entity_type=None):
        """
        Inicia modo spawn de entidades: picker parpadeante y selección inicial.
        """
        self.model.spawn_mode_active = True
        self.model.spawn_entity_type = entity_type
        # Iniciar parpadeo en picker
        self.picker_controller.model.blink = True
        # Mostrar picker
        self.picker_controller.model.visible = True
        # Reset selección previa
        self.picker_controller.model.selected_id = None

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

    def is_active(self, tool: str) -> bool:
        """Retorna True si la herramienta está activa en el toolbar."""
        return self.model.toolbar_model.active_tool == tool

    def handle_event(self, event: pygame.event.Event) -> bool:
        # Debug global entities_on_map: click event recibido
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            print(f"[DEBUG][EntitiesEditorController] Click global en {event.pos}, spawn_mode={self.model.spawn_mode_active}, spawn_entity_type={self.model.spawn_entity_type}")

        """
        Delega el evento a los subcontrollers en orden de prioridad.
        Retorna True si fue consumido.
        """
        if self.title_controller.handle_event(event):
            return True
        if self.toolbar_controller.handle_event(event):
            return True
        active = self.model.toolbar_model.active_tool
        if active in ('entities_on_map', 'entities_on_system'):
            # Add/Remove panel
            if self.add_remove_controller.handle_event(event):
                return True
            # Picker panel
            self.picker_controller.handle_event(event)
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
            # Properties panel
            # Sincronizar seleccionado
            self.properties_controller.model.selected_id = self.picker_controller.model.selected_id
            if self.properties_controller.handle_event(event):
                return True
            # Completando spawn: click en mapa finaliza spawn_mode
            if self.model.spawn_mode_active and self.model.spawn_entity_type and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                etype = self.model.spawn_entity_type
                sx, sy = event.pos
                cam = self.game.camera
                wx = sx / cam.zoom + cam.offset_x
                wy = sy / cam.zoom + cam.offset_y
                tx = int(wx // TILE_SIZE)
                ty = int(wy // TILE_SIZE)
                # Crear entidad en ECS
                if etype in self.model.player_stats:
                    get_factory("player").create(self.game.ecs.ecs_world, tile_x=tx, tile_y=ty, class_player=etype)
                else:
                    get_factory("monster").create(self.game.ecs.ecs_world, tile_x=tx, tile_y=ty, monster_type=etype)
                print(f"[DEBUG][EntitiesEditorController] Entity '{etype}' spawned at tile ({tx},{ty})")
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
