from roguelike_editors.buildings.building_editor_model import BuildingsEditorModel
from roguelike_editors.buildings.building_editor_controller import BuildingEditorController
from roguelike_editors.buildings.building_editor_events import BuildingEditorEventHandler
from roguelike_editors.buildings.building_editor_view import BuildingEditorView
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_model import BuildingsToolBarPanelModel
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_view import BuildingsToolBarPanelView
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_events import BuildingsToolBarPanelEventHandler
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_controller import BuildingsToolBarPanelController
from roguelike_engine.config.map_config import global_map_settings

class BuildingEditorManager:
    def __init__(self, game):
        # guardamos referencia al Game completo
        self.game = game
        state = game.state
        # tomamos la lista de edificios para pasarla también al event handler
        buildings = game.buildings.buildings

        # Inicialización del editor de edificios
        self.editor_state = BuildingsEditorModel()
        self.controller   = BuildingEditorController(state, self.editor_state, buildings, game.camera)
        self.view         = BuildingEditorView(state, self.editor_state)

        # Ahora el event handler recibe también la lista de buildings

        # pasamos también los offsets de cada zona        
        self.handler      = BuildingEditorEventHandler(
            state,
            self.editor_state,
            self.controller,
            buildings,
            zone_offsets= global_map_settings.zone_offsets
        )

        # --- Buildings Toolbar Panel ---
        # Crear toolbar (modelo, vista, eventos, controlador) siguiendo patrón Items
        self.buildings_toolbar_model = BuildingsToolBarPanelModel()
        # Construir vista y events con controlador placeholder y reinyectar después (resuelve circularidad)
        tmp_view = BuildingsToolBarPanelView(None, self.buildings_toolbar_model)
        tmp_events = BuildingsToolBarPanelEventHandler(None, self.buildings_toolbar_model)
        self.buildings_toolbar_controller = BuildingsToolBarPanelController(
            self, self.buildings_toolbar_model, tmp_view, tmp_events
        )
        # Reinyectar referencias al controlador real en vista y eventos
        tmp_view.controller = self.buildings_toolbar_controller
        # Asegurar que el widget compartido ToolbarView también tenga el controlador correcto
        try:
            if hasattr(tmp_view, 'widget'):
                tmp_view.widget.controller = self.buildings_toolbar_controller
        except Exception:
            pass
        tmp_events.controller = self.buildings_toolbar_controller
        # Permitir al event handler del editor delegar a la toolbar
        try:
            self.handler.buildings_toolbar_controller = self.buildings_toolbar_controller
        except Exception:
            pass

        # exponemos el state para que el Game lo use
        state.editor = self.editor_state

    def handle(self, camera, entities, events=None):
        self.handler.handle(camera, entities, events)

    def update(self, camera):
        if self.editor_state.active:
            self.controller.update(camera)

    def render(self, screen, camera, buildings):
        if self.editor_state.active:
            # Render principal del editor (incluye título y picker/overlays)
            self.view.render(screen, camera, buildings)
            # Render de la toolbar (centrada bajo el título)
            try:
                self.buildings_toolbar_controller.render(screen)
            except Exception:
                pass