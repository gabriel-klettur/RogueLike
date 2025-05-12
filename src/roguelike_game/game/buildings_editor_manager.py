#Path: src/roguelike_game/game/building_editor_manager.py

from roguelike_game.systems.editor.buildings.model.building_editor_state import BuildingsEditorState
from roguelike_game.systems.editor.buildings.controller.building_editor_controller import BuildingEditorController
from roguelike_game.systems.editor.buildings.events.building_editor_events import BuildingEditorEventHandler
from roguelike_game.systems.editor.buildings.view.building_editor_view import BuildingEditorView

class BuildingEditorManager:
    def __init__(self, game):
        # guardamos referencia al Game completo
        self.game = game
        state = game.state
        buildings = game.entities.buildings

        # Inicialización del editor de edificios
        self.editor_state = BuildingsEditorState()
        self.controller   = BuildingEditorController(state, self.editor_state, buildings)
        self.view         = BuildingEditorView(state, self.editor_state)
        self.handler      = BuildingEditorEventHandler(state, self.editor_state, self.controller)

        # exponemos el state para que el Game lo use
        state.editor = self.editor_state

    def handle(self, camera, entities):
        if self.editor_state.active:
            self.handler.handle(camera, entities)

    def update(self, camera):
        if self.editor_state.active:
            self.controller.update(camera)

    def render(self, screen, camera, buildings):
        if self.editor_state.active:
            self.view.render(screen, camera, buildings)
