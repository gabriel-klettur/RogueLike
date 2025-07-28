from roguelike_editors.buildings.building_editor_state import BuildingsEditorState
from roguelike_editors.buildings.building_editor_controller import BuildingEditorController
from roguelike_editors.buildings.building_editor_events import BuildingEditorEventHandler
from roguelike_editors.buildings.building_editor_view import BuildingEditorView
from roguelike_engine.config.map_config import global_map_settings

class BuildingEditorManager:
    def __init__(self, game):
        # guardamos referencia al Game completo
        self.game = game
        state = game.state
        # tomamos la lista de edificios para pasarla también al event handler
        buildings = game.buildings.buildings

        # Inicialización del editor de edificios
        self.editor_state = BuildingsEditorState()
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

        # exponemos el state para que el Game lo use
        state.editor = self.editor_state

    def handle(self, camera, entities, events=None):
        self.handler.handle(camera, entities, events)

    def update(self, camera):
        if self.editor_state.active:
            self.controller.update(camera)

    def render(self, screen, camera, buildings):
        if self.editor_state.active:
            self.view.render(screen, camera, buildings)