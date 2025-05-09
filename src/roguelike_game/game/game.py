#Path: roguelike_game/game/game.py

import pygame

#!---------------------- Paquetes locales: configuración --------------------------------
import roguelike_engine.config as config

#!------------------------ Paquetes locales: motor (engine) -----------------------------------
from roguelike_engine.camera.camera import Camera
from roguelike_engine.input.events import handle_events


#!-------------------- Paquetes locales: lógica de juego principal ----------------------------
from roguelike_game.game.state import GameState
from roguelike_game.game.render_manager import Renderer
from roguelike_game.game.update_manager import update_game

#!----------------------------- Paquetes locales: managers ------------------------------------
from roguelike_game.game.map_manager import MapManager
from roguelike_game.game.entities_manager import EntitiesManager
from roguelike_game.game.z_layer_manager import ZLayerManager

#!----------------------- Paquetes locales: entidades y sistemas ------------------------------
from roguelike_game.network.multiplayer_manager import NetworkManager
from roguelike_game.systems.systems_manager import SystemsManager

#!-------------------------- Paquetes locales: menús e interfaz -------------------------------
from roguelike_game.ui.menus.menu import Menu

#! --------------------- Paquetes locales: editores (building) --------------------------------
from roguelike_game.systems.editor.buildings.model.building_editor_state import (
    BuildingsEditorState,
)
from roguelike_game.systems.editor.buildings.controller.building_editor_controller import (
    BuildingEditorController,
)
from roguelike_game.systems.editor.buildings.controller.building_editor_events import (
    BuildingEditorEventHandler,
)
from roguelike_game.systems.editor.buildings.view.building_editor_view import (
    BuildingEditorView,
)

#! --------------------- Paquetes locales: editores (tile) -------------------------------------
from roguelike_game.systems.editor.tiles.model.tile_editor_state import (
    TileEditorControllerState,
)
from roguelike_game.systems.editor.tiles.controller.tile_editor_controller import (
    TileEditorController,
)
from roguelike_game.systems.editor.tiles.controller.tile_editor_events import (
    TileEditorEventHandler,
)
from roguelike_game.systems.editor.tiles.view.tile_editor_view import (
    TileEditorControllerView,
)

#! -------------------------- Paquetes locales: z-layer ----------------------------------------
from roguelike_game.systems.z_layer.state import ZState

class Game:
    def __init__(self, screen, perf_log=None, map_name: str = None):        
        #! ------------- infraestructura -----------------
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
        self.camera = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
        self.z_state = ZState()
        self.perf_log = perf_log

        #! ---------------- core state -------------------
        self._init_state()

        
        #! ------------------ systems --------------------
        self._init_map(map_name)
        self._init_entities() 
        self._init_z_layer(self.entities)
        

        self._init_systems(self.perf_log)

        #! ------------- editores ------------------------
        self._init_building_editor()
        self._init_tile_editor()

    def _init_state(self):
        """
        Inicializa el estado del juego
        """
        self.state = GameState()

        

    def _init_map(self, map_name: str | None):
        """
        Construye el mapa global y carga todos sus datos en el estado.
        """        
        self.map = MapManager(map_name)                                        


    def _init_entities(self):
        """
        Inicializa el gestor de entidades (player, enemies, obstacles y buildings) y carga los datos en el estado.
        """
        self.entities = EntitiesManager(self.z_state, self.map)
        

    def _init_z_layer(self, entities):
        """
        Inicializa el gestor de capas Z y asigna las capas a las entidades.
        """
        self.zlayer = ZLayerManager(self.z_state)
        self.zlayer.initialize(self.state, entities)

    def _init_systems(self, perf_log):
        self.renderer = Renderer()
        self.entities.player.renderer.state = self.state
        self.entities.player.state = self.state
        self.menu = Menu(self.state)
        self.state.show_menu = False
        self.state.mode = "local"
        self.systems = SystemsManager(self.state, perf_log)
        self.state.systems = self.systems
        self.state.effects = self.systems.effects
        self.network = NetworkManager(self.state)
        if self.state.mode == "online":
            self.network.connect()

    def _init_building_editor(self):
        self.editor_state = BuildingsEditorState()
        self.building_editor = BuildingEditorController(self.state, self.editor_state, self.entities.buildings)
        self.building_editor_view = BuildingEditorView(self.state, self.editor_state)        
        self.building_event_handler = BuildingEditorEventHandler(self.state, self.editor_state, self.building_editor)
        self.state.editor = self.editor_state

    def _init_tile_editor(self):
        self.tile_editor_state = TileEditorControllerState()
        self.tile_editor = TileEditorController(self.state, self.tile_editor_state)
        self.tile_editor_view = TileEditorControllerView(self.tile_editor, self.state, self.tile_editor_state)        
        self.tile_event_handler = TileEditorEventHandler(self.state, self.tile_editor_state, self.tile_editor)
        self.state.tile_editor_active = False
        self.state.tile_editor = self.tile_editor
        self.state.tile_editor_state = self.tile_editor_state
        self.state.tile_editor_view = self.tile_editor_view

    def handle_events(self):
        if self.tile_editor_state.active:
            self.tile_event_handler.handle(self.camera, self.map)
            return
        if self.state.editor.active:
            self.building_event_handler.handle(self.camera, self.entities)
            return
        handle_events(self.state, self.camera, self.clock, self.menu, self.map, self.entities)

    def update(self):
        if self.tile_editor_state.active:
            return
        if self.state.editor.active:
            self.building_editor.update(self.camera)
        else:
            update_game(self.state, self.systems, self.camera, self.clock, self.screen, self.map, self.entities)

    def render(self, perf_log=None):
        self.renderer.render_game(self.state, self.screen, self.camera, perf_log, self.menu, self.map, self.entities)
        if self.state.editor.active:
            self.building_editor_view.render(self.screen, self.camera, self.entities.buildings)
        if self.tile_editor_state.active:
            self.tile_editor_view.render(self.screen, self.camera, self.map)

    def quit(self):
        if hasattr(self, 'network'):
            self.network.disconnect()
        pygame.quit()
